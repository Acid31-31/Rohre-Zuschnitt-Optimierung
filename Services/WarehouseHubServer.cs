using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Lager-Zentrale: ein PC hostet die SQLite-DB und beantwortet HTTP-Anfragen der anderen PCs.
/// </summary>
internal static class WarehouseHubServer
{
  private static readonly object Gate = new();
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  private static HttpListener? _listener;
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
        return;

      if (port is < 1 or > 65535)
        port = 5088;

      WarehouseSqliteStore.EnsureInitialized();

      var listener = new HttpListener();
      foreach (var prefix in BuildPrefixes(port, includeWildcard: true))
        listener.Prefixes.Add(prefix);

      try
      {
        listener.Start();
      }
      catch (HttpListenerException)
      {
        listener.Close();
        listener = new HttpListener();
        foreach (var prefix in BuildPrefixes(port, includeWildcard: false))
          listener.Prefixes.Add(prefix);
        listener.Start();
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
      if (!IsRunning)
        return;

      try { _cts?.Cancel(); } catch { }
      try { _listener?.Stop(); } catch { }
      try { _listener?.Close(); } catch { }
      _listener = null;
      _cts = null;
      _loop = null;
      IsRunning = false;
    }
  }

  private static async Task ListenLoopAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      HttpListenerContext context;
      try
      {
        context = await _listener!.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
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

      _ = Task.Run(() => HandleRequest(context), cancellationToken);
    }
  }

  private static void HandleRequest(HttpListenerContext context)
  {
    try
    {
      var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
      if (string.Equals(path, "/api/health", StringComparison.OrdinalIgnoreCase)
          || string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
      {
        WriteJson(context, 200, new { ok = true, role = "warehouse-hub", version = WarehouseSqliteStore.GetCurrentVersion() });
        return;
      }

      if (string.Equals(path, "/api/warehouse", StringComparison.OrdinalIgnoreCase))
      {
        if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
          var (version, items) = WarehouseSqliteStore.Load();
          WriteJson(context, 200, ToDto(version, items));
          return;
        }

        if (context.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
          using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
          var body = reader.ReadToEnd();
          var snapshot = JsonSerializer.Deserialize<WarehouseSnapshotDto>(body, JsonOptions)
                         ?? throw new InvalidOperationException("Ungültiger Lager-Inhalt.");
          var items = FromDto(snapshot.Items);
          try
          {
            var next = WarehouseSqliteStore.Save(items, snapshot.Version);
            var (_, loaded) = WarehouseSqliteStore.Load();
            WriteJson(context, 200, ToDto(next, loaded));
          }
          catch (InvalidOperationException ex)
          {
            var (version, current) = WarehouseSqliteStore.Load();
            WriteJson(context, 409, new
            {
              error = ex.Message,
              snapshot = ToDto(version, current)
            });
          }

          return;
        }
      }

      WriteJson(context, 404, new { error = "Nicht gefunden." });
    }
    catch (Exception ex)
    {
      try { WriteJson(context, 500, new { error = ex.Message }); }
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

  private static IEnumerable<string> BuildPrefixes(int port, bool includeWildcard)
  {
    var prefixes = new List<string>
    {
      $"http://127.0.0.1:{port}/",
      $"http://localhost:{port}/"
    };

    try
    {
      foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
      {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
          continue;
        if (IPAddress.IsLoopback(address))
          continue;
        prefixes.Add($"http://{address}:{port}/");
      }
    }
    catch
    {
    }

    if (includeWildcard)
      prefixes.Add($"http://+:{port}/");

    return prefixes;
  }

  private static void WriteJson(HttpListenerContext context, int statusCode, object payload)
  {
    var json = JsonSerializer.Serialize(payload, JsonOptions);
    var bytes = Encoding.UTF8.GetBytes(json);
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.ContentLength64 = bytes.Length;
    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    context.Response.OutputStream.Close();
  }
}
