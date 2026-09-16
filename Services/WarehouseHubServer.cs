using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Lager-Zentrale über TcpListener (kein Admin / kein URL-ACL wie bei HttpListener).
/// </summary>
internal static class WarehouseHubServer
{
  private static readonly object Gate = new();
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  private static TcpListener? _listener;
  private static CancellationTokenSource? _cts;
  private static Task? _loop;
  private static int _port;

  public static bool IsRunning { get; private set; }

  public static int Port => _port;

  public static void Start(int port)
  {
    lock (Gate)
    {
      if (IsRunning)
        StopUnlocked();

      if (port is < 1 or > 65535)
        port = 5088;

      WarehouseSqliteStore.EnsureInitialized();
      WarehousePresenceRegistry.Heartbeat(WarehousePresenceRegistry.CreateLocalPeer("host"));

      var listener = new TcpListener(IPAddress.Any, port);
      listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      try
      {
        listener.Start();
      }
      catch (SocketException ex)
      {
        throw new InvalidOperationException(
          "Port " + port + " konnte nicht geöffnet werden: " + ex.Message
          + Environment.NewLine + "Anderer Dienst belegt den Port? Anderen Port versuchen oder Firewall prüfen.",
          ex);
      }

      _listener = listener;
      _port = port;
      _cts = new CancellationTokenSource();
      IsRunning = true;
      _loop = Task.Run(() => ListenLoopAsync(_cts.Token));
    }
  }

  public static void Stop()
  {
    lock (Gate)
    {
      StopUnlocked();
    }
  }

  private static void StopUnlocked()
  {
    if (!IsRunning && _listener is null)
      return;

    try { _cts?.Cancel(); } catch { }
    try { _listener?.Stop(); } catch { }
    _listener = null;
    _cts = null;
    _loop = null;
    IsRunning = false;
  }

  private static async Task ListenLoopAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      TcpClient client;
      try
      {
        client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch
      {
        if (cancellationToken.IsCancellationRequested)
          break;
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        continue;
      }

      _ = Task.Run(() => HandleClient(client), cancellationToken);
    }
  }

  private static void HandleClient(TcpClient client)
  {
    using (client)
    {
      try
      {
        client.ReceiveTimeout = 8000;
        client.SendTimeout = 8000;
        using var stream = client.GetStream();
        var (method, path, body) = ReadHttpRequest(stream);
        HandleRequest(stream, method, path, body);
      }
      catch
      {
      }
    }
  }

  private static (string Method, string Path, string Body) ReadHttpRequest(NetworkStream stream)
  {
    var buffer = new MemoryStream();
    var chunk = new byte[4096];
    var headerEnd = -1;
    while (headerEnd < 0)
    {
      var read = stream.Read(chunk, 0, chunk.Length);
      if (read <= 0)
        break;
      buffer.Write(chunk, 0, read);
      var bytes = buffer.ToArray();
      headerEnd = IndexOfHeaderEnd(bytes);
      if (headerEnd >= 0)
        break;
      if (buffer.Length > 1024 * 1024)
        throw new InvalidOperationException("HTTP-Header zu groß.");
    }

    var raw = buffer.ToArray();
    if (headerEnd < 0)
      throw new InvalidOperationException("Unvollständige HTTP-Anfrage.");

    var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
    var lines = headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    if (lines.Length == 0)
      throw new InvalidOperationException("Leere HTTP-Anfrage.");

    var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var method = parts.Length > 0 ? parts[0] : "GET";
    var pathWithQuery = parts.Length > 1 ? parts[1] : "/";
    var path = pathWithQuery.Split('?', 2)[0];

    var contentLength = 0;
    foreach (var line in lines.Skip(1))
    {
      if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
          && int.TryParse(line["Content-Length:".Length..].Trim(), out var len))
        contentLength = Math.Max(0, len);
    }

    var bodyStart = headerEnd + 4;
    var bodyBuilder = new MemoryStream();
    if (bodyStart < raw.Length)
      bodyBuilder.Write(raw, bodyStart, raw.Length - bodyStart);

    while (bodyBuilder.Length < contentLength)
    {
      var read = stream.Read(chunk, 0, chunk.Length);
      if (read <= 0)
        break;
      bodyBuilder.Write(chunk, 0, read);
    }

    var bodyBytes = bodyBuilder.ToArray();
    if (bodyBytes.Length > contentLength && contentLength > 0)
      Array.Resize(ref bodyBytes, contentLength);

    var body = bodyBytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bodyBytes);
    return (method, path, body);
  }

  private static int IndexOfHeaderEnd(byte[] bytes)
  {
    for (var i = 0; i + 3 < bytes.Length; i++)
    {
      if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n'
          && bytes[i + 2] == (byte)'\r' && bytes[i + 3] == (byte)'\n')
        return i;
    }

    return -1;
  }

  private static void HandleRequest(NetworkStream stream, string method, string path, string body)
  {
    try
    {
      path = (path ?? string.Empty).TrimEnd('/');
      if (string.Equals(path, "/api/health", StringComparison.OrdinalIgnoreCase)
          || string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
      {
        WriteJson(stream, 200, new { ok = true, role = "warehouse-hub", version = WarehouseSqliteStore.GetCurrentVersion() });
        return;
      }

      if (string.Equals(path, "/api/presence", StringComparison.OrdinalIgnoreCase))
      {
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
          WarehousePresenceRegistry.Heartbeat(WarehousePresenceRegistry.CreateLocalPeer("host"));
          WriteJson(stream, 200, new WarehousePresenceSnapshotDto
          {
            Peers = WarehousePresenceRegistry.GetActive().ToList()
          });
          return;
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
          var peer = JsonSerializer.Deserialize<WarehousePresenceDto>(body, JsonOptions)
                     ?? throw new InvalidOperationException("Ungültige Präsenz-Daten.");
          if (string.IsNullOrWhiteSpace(peer.Role))
            peer.Role = "client";
          WarehousePresenceRegistry.Heartbeat(peer);
          WarehousePresenceRegistry.Heartbeat(WarehousePresenceRegistry.CreateLocalPeer("host"));
          WriteJson(stream, 200, new WarehousePresenceSnapshotDto
          {
            Peers = WarehousePresenceRegistry.GetActive().ToList()
          });
          return;
        }
      }

      if (string.Equals(path, "/api/warehouse", StringComparison.OrdinalIgnoreCase))
      {
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
          var (version, items) = WarehouseSqliteStore.Load();
          WriteJson(stream, 200, ToDto(version, items));
          return;
        }

        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
          var snapshot = JsonSerializer.Deserialize<WarehouseSnapshotDto>(body, JsonOptions)
                         ?? throw new InvalidOperationException("Ungültiger Lager-Inhalt.");
          var items = FromDto(snapshot.Items);
          try
          {
            var next = WarehouseSqliteStore.Save(items, snapshot.Version);
            var (_, loaded) = WarehouseSqliteStore.Load();
            WriteJson(stream, 200, ToDto(next, loaded));
          }
          catch (InvalidOperationException ex)
          {
            var (version, current) = WarehouseSqliteStore.Load();
            WriteJson(stream, 409, new
            {
              error = ex.Message,
              snapshot = ToDto(version, current)
            });
          }

          return;
        }
      }

      WriteJson(stream, 404, new { error = "Nicht gefunden." });
    }
    catch (Exception ex)
    {
      try { WriteJson(stream, 500, new { error = ex.Message }); }
      catch { }
    }
  }

  private static WarehouseSnapshotDto ToDto(long version, IEnumerable<PipeWarehouseStockItem> items) =>
    new()
    {
      Version = version,
      Items = items.Select(item => new WarehouseStockDto
      {
        ProfileId = item.ProfileId,
        Material = item.Material,
        LengthMm = item.LengthMm,
        Quantity = item.Quantity,
        ReservedQuantity = item.ReservedQuantity
      }).ToList()
    };

  private static List<PipeWarehouseStockItem> FromDto(IEnumerable<WarehouseStockDto> items) =>
    items.Select(item => new PipeWarehouseStockItem
    {
      ProfileId = item.ProfileId,
      Material = item.Material,
      LengthMm = item.LengthMm,
      Quantity = item.Quantity,
      ReservedQuantity = item.ReservedQuantity
    }).ToList();

  private static void WriteJson(NetworkStream stream, int statusCode, object payload)
  {
    var json = JsonSerializer.Serialize(payload, JsonOptions);
    var bytes = Encoding.UTF8.GetBytes(json);
    var reason = statusCode switch
    {
      200 => "OK",
      404 => "Not Found",
      409 => "Conflict",
      500 => "Internal Server Error",
      _ => "OK"
    };
    var header = $"HTTP/1.1 {statusCode} {reason}\r\n"
                 + "Content-Type: application/json; charset=utf-8\r\n"
                 + "Content-Length: " + bytes.Length + "\r\n"
                 + "Connection: close\r\n"
                 + "Access-Control-Allow-Origin: *\r\n"
                 + "\r\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    stream.Write(headerBytes, 0, headerBytes.Length);
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
  }
}