using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Gemeinsames Lager im Netzwerkordner (XML). Mehrere PCs dürfen gleichzeitig lesen/schreiben.
/// Speichern erfolgt atomar (Temp + Replace), ohne exklusive Dauer-Sperre.
/// </summary>
internal static class WarehouseSharedFolderStore
{
  private static readonly object Gate = new();
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
  };

  private const string StockFileName = "pipe-warehouse.xml";
  private const string OnlineFolderName = "online";
  private const string PathMarkerFileName = "lager-netzwerkpfad.txt";
  private static readonly TimeSpan PresenceOfflineAfter = TimeSpan.FromSeconds(12);

  private static string? _currentDirectory;
  private static bool _initialized;

  public static event Action? ExternalChanged;

  public static string? CurrentDirectory => _currentDirectory;

  public static string GetStockFilePath(string directory) =>
    Path.Combine(directory, StockFileName);

  public static void EnsureInitialized(string directory, bool seedFromLocalIfEmpty = true)
  {
    var root = NormalizeDirectory(directory);
    lock (Gate)
    {
      if (_initialized
          && string.Equals(_currentDirectory, root, StringComparison.OrdinalIgnoreCase))
        return;

      Directory.CreateDirectory(root);
      Directory.CreateDirectory(Path.Combine(root, OnlineFolderName));
      _currentDirectory = root;
      TryWritePathMarker(root);

      var stockPath = GetStockFilePath(root);
      if (!File.Exists(stockPath) || new FileInfo(stockPath).Length == 0)
      {
        List<PipeWarehouseStockItem> seed = [];
        if (seedFromLocalIfEmpty)
        {
          try
          {
            WarehouseSqliteStore.EnsureInitialized();
            seed = WarehouseSqliteStore.Load().Items;
          }
          catch
          {
          }
        }

        if (seed.Count == 0)
        {
          seed = PipeStockCatalog.All
            .Select(profile => new PipeWarehouseStockItem
            {
              ProfileId = profile.Id,
              Material = profile.Material,
              LengthMm = CutOptimizationDefaults.StockLengthMm,
              Quantity = 0
            })
            .ToList();
        }

        AtomicWrite(stockPath, version: 1, seed);
      }

      _initialized = true;
    }
  }

  public static void StopWatcher()
  {
    lock (Gate)
    {
      _initialized = false;
      _currentDirectory = null;
    }
  }

  public static (long Version, List<PipeWarehouseStockItem> Items) Load(string directory)
  {
    var root = NormalizeDirectory(directory);
    EnsureInitialized(root, seedFromLocalIfEmpty: true);
    var path = GetStockFilePath(root);
    var (version, items) = ReadWithRetry(path);
    PipeWarehouseStore.RefreshDisplayNames(items);
    return (version, items);
  }

  public static long Save(
    string directory,
    IEnumerable<PipeWarehouseStockItem> items,
    long? expectedVersion,
    IReadOnlyCollection<string>? loadedStockKeys = null)
  {
    var root = NormalizeDirectory(directory);
    Directory.CreateDirectory(root);
    var local = items.ToList();
    var path = GetStockFilePath(root);

    Exception? last = null;
    for (var attempt = 0; attempt < 16; attempt++)
    {
      try
      {
        lock (Gate)
        {
          var remote = ReadFileOrEmpty(path);
          var merged = MergeStock(local, remote.Items, loadedStockKeys);
          var next = Math.Max(remote.Version, expectedVersion ?? 0) + 1;
          AtomicWrite(path, next, merged);
          return next;
        }
      }
      catch (IOException ex)
      {
        last = ex;
        Thread.Sleep(80 + attempt * 50);
      }
      catch (UnauthorizedAccessException ex)
      {
        last = ex;
        Thread.Sleep(80 + attempt * 50);
      }
    }

    throw new InvalidOperationException(
      "Lager konnte nicht gespeichert werden (Netzwerk beschäftigt). Bitte erneut versuchen."
      + Environment.NewLine + path
      + Environment.NewLine + (last?.Message ?? string.Empty),
      last);
  }

  public static long GetCurrentVersion(string directory)
  {
    var root = NormalizeDirectory(directory);
    var path = GetStockFilePath(root);
    if (!File.Exists(path))
      return 0;
    return ReadWithRetry(path).Version;
  }

  public static void HeartbeatPresence(string directory, WarehousePresenceDto self)
  {
    var root = NormalizeDirectory(directory);
    Directory.CreateDirectory(Path.Combine(root, OnlineFolderName));
    self.LastSeenUtc = DateTimeOffset.UtcNow;
    if (string.IsNullOrWhiteSpace(self.DisplayName))
      self.DisplayName = WarehousePresenceRegistry.BuildDisplayName(self.UserName, self.MachineName);
    if (string.IsNullOrWhiteSpace(self.Role))
      self.Role = "shared";

    var file = GetPresenceFilePath(root, self.ClientId);
    var json = JsonSerializer.Serialize(self, JsonOptions);
    var localTemp = Path.Combine(Path.GetTempPath(), "rohre-online-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
      File.WriteAllText(localTemp, json);
      for (var attempt = 0; attempt < 8; attempt++)
      {
        try
        {
          File.Copy(localTemp, file, overwrite: true);
          return;
        }
        catch (IOException)
        {
          Thread.Sleep(40 + attempt * 30);
        }
        catch (UnauthorizedAccessException)
        {
          Thread.Sleep(40 + attempt * 30);
        }
      }
    }
    finally
    {
      TryDelete(localTemp);
    }
  }

  public static IReadOnlyList<WarehousePresenceDto> GetActivePresence(string directory)
  {
    var root = NormalizeDirectory(directory);
    var onlineDir = Path.Combine(root, OnlineFolderName);
    if (!Directory.Exists(onlineDir))
      return [];

    var cutoff = DateTimeOffset.UtcNow - PresenceOfflineAfter;
    var list = new List<WarehousePresenceDto>();
    foreach (var file in Directory.EnumerateFiles(onlineDir, "*.json"))
    {
      try
      {
        var peer = JsonSerializer.Deserialize<WarehousePresenceDto>(File.ReadAllText(file), JsonOptions);
        if (peer is null || string.IsNullOrWhiteSpace(peer.ClientId))
        {
          TryDelete(file);
          continue;
        }

        if (peer.LastSeenUtc < cutoff)
        {
          TryDelete(file);
          continue;
        }

        list.Add(peer);
      }
      catch
      {
        TryDelete(file);
      }
    }

    return list
      .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
  }

  private static List<PipeWarehouseStockItem> MergeStock(
    List<PipeWarehouseStockItem> local,
    List<PipeWarehouseStockItem> remote,
    IReadOnlyCollection<string>? loadedStockKeys)
  {
    var map = new Dictionary<string, PipeWarehouseStockItem>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in local.Where(i => !string.IsNullOrWhiteSpace(i.ProfileId) && i.LengthMm > 0))
      map[StockKey(item)] = CloneStock(item);

    // Ohne Baseline: lokale Liste ist maßgeblich (inkl. Löschen).
    if (loadedStockKeys is null)
      return map.Values.ToList();

    // Nur Zeilen behalten, die ein anderer PC NEU angelegt hat (nicht in unserem Lade-Stand).
    var loaded = new HashSet<string>(loadedStockKeys, StringComparer.OrdinalIgnoreCase);
    foreach (var item in remote.Where(i => !string.IsNullOrWhiteSpace(i.ProfileId) && i.LengthMm > 0))
    {
      var key = StockKey(item);
      if (map.ContainsKey(key))
        continue;
      if (loaded.Contains(key))
        continue; // bei uns gelöscht
      map[key] = CloneStock(item);
    }

    return map.Values.ToList();
  }

  public static string BuildStockKey(PipeWarehouseStockItem item) => StockKey(item);

  private static string StockKey(PipeWarehouseStockItem item) =>
    (item.ProfileId ?? string.Empty).Trim()
    + "|"
    + (item.Material ?? PipeMaterialTypes.Steel).Trim()
    + "|"
    + item.LengthMm.ToString("0.###", CultureInfo.InvariantCulture);

  private static PipeWarehouseStockItem CloneStock(PipeWarehouseStockItem item) =>
    new()
    {
      ProfileId = item.ProfileId,
      Material = item.Material,
      LengthMm = item.LengthMm,
      Quantity = item.Quantity,
      ReservedQuantity = item.ReservedQuantity,
    };

  private static (long Version, List<PipeWarehouseStockItem> Items) ReadWithRetry(string path)
  {
    Exception? last = null;
    for (var attempt = 0; attempt < 12; attempt++)
    {
      try
      {
        return ReadFileOrEmpty(path);
      }
      catch (IOException ex)
      {
        last = ex;
        Thread.Sleep(40 + attempt * 35);
      }
      catch (UnauthorizedAccessException ex)
      {
        last = ex;
        Thread.Sleep(40 + attempt * 35);
      }
    }

    throw new InvalidOperationException(
      "Lager-Datei ist vorübergehend nicht lesbar: " + path
      + Environment.NewLine + (last?.Message ?? string.Empty),
      last);
  }

  private static (long Version, List<PipeWarehouseStockItem> Items) ReadFileOrEmpty(string path)
  {
    if (!File.Exists(path) || new FileInfo(path).Length == 0)
      return (0, []);

    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    return ReadXml(stream);
  }

  private static void AtomicWrite(string path, long version, IEnumerable<PipeWarehouseStockItem> items)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    CleanupStaleTempFiles(path);

    // XML zuerst lokal schreiben – File.Replace/.tmp direkt auf SMB verursacht
    // oft „Nicht genügend Systemressourcen“.
    byte[] payload;
    using (var memory = new MemoryStream())
    {
      WriteXml(memory, version, items);
      payload = memory.ToArray();
    }

    var localTemp = Path.Combine(Path.GetTempPath(), "rohre-lager-" + Guid.NewGuid().ToString("N") + ".xml");
    try
    {
      File.WriteAllBytes(localTemp, payload);

      Exception? last = null;
      for (var attempt = 0; attempt < 20; attempt++)
      {
        try
        {
          File.Copy(localTemp, path, overwrite: true);
          return;
        }
        catch (IOException ex)
        {
          last = ex;
          Thread.Sleep(100 + attempt * 60);
        }
        catch (UnauthorizedAccessException ex)
        {
          last = ex;
          Thread.Sleep(100 + attempt * 60);
        }
      }

      // Letzter Versuch: direkt auf Zielstream mit ReadWrite-Share
      try
      {
        using var stream = new FileStream(
          path,
          FileMode.Create,
          FileAccess.Write,
          FileShare.ReadWrite | FileShare.Delete);
        stream.Write(payload, 0, payload.Length);
        stream.Flush(true);
        return;
      }
      catch (Exception ex)
      {
        throw new IOException(
          "Schreiben auf Netzwerkordner fehlgeschlagen: " + path
          + Environment.NewLine + (last?.Message ?? ex.Message),
          last ?? ex);
      }
    }
    finally
    {
      TryDelete(localTemp);
    }
  }

  private static void CleanupStaleTempFiles(string stockPath)
  {
    try
    {
      var dir = Path.GetDirectoryName(stockPath);
      if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        return;
      var prefix = Path.GetFileName(stockPath) + ".";
      foreach (var file in Directory.EnumerateFiles(dir, Path.GetFileName(stockPath) + ".*.tmp"))
      {
        try
        {
          if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-10))
            File.Delete(file);
        }
        catch
        {
        }
      }
    }
    catch
    {
    }
  }

  private static (long Version, List<PipeWarehouseStockItem> Items) ReadXml(Stream stream)
  {
    stream.Position = 0;
    if (stream.Length == 0)
      return (0, []);

    var document = XDocument.Load(stream);
    var root = document.Root;
    if (root is null)
      return (0, []);

    var version = long.TryParse((string?)root.Attribute("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
      ? v
      : 1;

    var items = new List<PipeWarehouseStockItem>();
    foreach (var element in root.Elements("Stock"))
    {
      var profileId = (string?)element.Attribute("profileId") ?? string.Empty;
      if (string.IsNullOrWhiteSpace(profileId))
        continue;

      var length = double.TryParse(
        (string?)element.Attribute("lengthMm"),
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out var parsedLength)
        ? parsedLength
        : 0;
      if (length <= 0)
        continue;

      items.Add(new PipeWarehouseStockItem
      {
        ProfileId = profileId,
        Material = (string?)element.Attribute("material") ?? PipeMaterialTypes.Steel,
        LengthMm = length,
        Quantity = int.TryParse((string?)element.Attribute("quantity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : 0,
        ReservedQuantity = int.TryParse((string?)element.Attribute("reservedQuantity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0
      });
    }

    return (version, items);
  }

  private static void WriteXml(Stream stream, long version, IEnumerable<PipeWarehouseStockItem> items)
  {
    var root = new XElement(
      "Warehouse",
      new XAttribute("version", version.ToString(CultureInfo.InvariantCulture)));

    foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.ProfileId) && i.LengthMm > 0))
    {
      root.Add(new XElement(
        "Stock",
        new XAttribute("profileId", item.ProfileId),
        new XAttribute("material", item.Material ?? PipeMaterialTypes.Steel),
        new XAttribute("lengthMm", item.LengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
        new XAttribute("quantity", Math.Max(0, item.Quantity).ToString(CultureInfo.InvariantCulture)),
        new XAttribute("reservedQuantity", Math.Max(0, item.ReservedQuantity).ToString(CultureInfo.InvariantCulture))));
    }

    var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    document.Save(stream);
  }

  private static string NormalizeDirectory(string directory)
  {
    if (string.IsNullOrWhiteSpace(directory))
      throw new InvalidOperationException("Kein gemeinsamer Lager-Ordner konfiguriert.");
    return Path.GetFullPath(directory.Trim().TrimEnd('\\', '/'));
  }

  private static string GetPresenceFilePath(string root, string clientId)
  {
    var safe = string.Join("_", (clientId ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    if (string.IsNullOrWhiteSpace(safe))
      safe = "unknown";
    if (safe.Length > 80)
      safe = safe[..80];
    return Path.Combine(root, OnlineFolderName, safe + ".json");
  }

  private static void TryWritePathMarker(string root)
  {
    try { File.WriteAllText(Path.Combine(root, PathMarkerFileName), root); }
    catch { }
  }

  private static void TryDelete(string path)
  {
    try { File.Delete(path); } catch { }
  }
}