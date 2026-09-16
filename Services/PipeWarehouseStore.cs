using System.Threading;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Fassade: lokal / gemeinsamer Ordner / optional Host-Client.
/// </summary>
public static class PipeWarehouseStore
{
  private static readonly object InitGate = new();
  private static long _knownVersion = 1;
  private static DispatcherPoller? _poller;
  private static bool _sharedWatcherHooked;
  private static bool _runtimeReady;
  private static string _runtimeKey = string.Empty;

  public static event Action? ExternalChanged;
  public static event Action? PresenceChanged;

  public static string FilePath
  {
    get
    {
      var settings = AppSettingsStore.Load();
      if (ParseMode(settings.WarehouseSyncMode) == WarehouseSyncMode.SharedFolder
          && !string.IsNullOrWhiteSpace(settings.SharedWarehouseDirectory))
        return WarehouseSharedFolderStore.GetStockFilePath(settings.SharedWarehouseDirectory);
      return WarehouseSqliteStore.DatabasePath;
    }
  }

  public static bool UsesSharedNetworkPath =>
    GetMode() is WarehouseSyncMode.SharedFolder or WarehouseSyncMode.Client;

  public static bool IsHubHost => GetMode() == WarehouseSyncMode.Host && WarehouseHubServer.IsRunning;

  public static List<PipeWarehouseStockItem> Load()
  {
    EnsureInitialized();
    var mode = GetMode();
    if (mode == WarehouseSyncMode.Client)
    {
      var url = GetClientUrl();
      var (version, items) = WarehouseHubClient.Load(url);
      _knownVersion = version;
      return items;
    }

    if (mode == WarehouseSyncMode.SharedFolder)
    {
      try
      {
        var (version, items) = WarehouseSharedFolderStore.Load(GetSharedDirectory());
        _knownVersion = version;
        return items;
      }
      catch
      {
        var local = WarehouseSqliteStore.Load();
        _knownVersion = local.Version;
        return local.Items;
      }
    }

    var sqlite = WarehouseSqliteStore.Load();
    _knownVersion = sqlite.Version;
    return sqlite.Items;
  }

  public static void Save(IEnumerable<PipeWarehouseStockItem> items, IReadOnlyCollection<string>? loadedStockKeys = null)
  {
    EnsureInitialized();
    var list = items.ToList();
    var mode = GetMode();
    if (mode == WarehouseSyncMode.Client)
    {
      var url = GetClientUrl();
      _knownVersion = WarehouseHubClient.Save(url, list, _knownVersion);
      return;
    }

    if (mode == WarehouseSyncMode.SharedFolder)
    {
      _knownVersion = WarehouseSharedFolderStore.Save(GetSharedDirectory(), list, expectedVersion: null, loadedStockKeys);
      return;
    }

    _knownVersion = WarehouseSqliteStore.Save(list, expectedVersion: null);
  }

  public static void EnsureInitialized()
  {
    var settings = AppSettingsStore.Load();
    ApplyRuntimeMode(settings);
  }

  public static void ApplyRuntimeMode(AppSettings settings)
  {
    var mode = ParseMode(settings.WarehouseSyncMode);
    var key = mode switch
    {
      WarehouseSyncMode.SharedFolder => "shared|" + (settings.SharedWarehouseDirectory ?? string.Empty).Trim(),
      WarehouseSyncMode.Host => "host|" + (settings.WarehouseHubPort > 0 ? settings.WarehouseHubPort : 5088),
      WarehouseSyncMode.Client => "client|" + WarehouseHubClient.NormalizeBaseUrl(settings.WarehouseHubUrl),
      _ => "local"
    };

    lock (InitGate)
    {
      if (_runtimeReady && string.Equals(_runtimeKey, key, StringComparison.OrdinalIgnoreCase))
        return;

      if (mode == WarehouseSyncMode.Host)
      {
        WarehouseSharedFolderStore.StopWatcher();
        try { WarehouseSqliteStore.EnsureInitialized(); } catch { }
        WarehouseHubServer.Stop();
        try
        {
          WarehouseHubServer.Start(settings.WarehouseHubPort > 0 ? settings.WarehouseHubPort : 5088);
        }
        catch
        {
        }
        StopPolling();
        _runtimeReady = true;
        _runtimeKey = key;
        return;
      }

      WarehouseHubServer.Stop();

      if (mode == WarehouseSyncMode.Client)
      {
        WarehouseSharedFolderStore.StopWatcher();
        StartHubPolling(settings.WarehouseHubUrl);
        _runtimeReady = true;
        _runtimeKey = key;
        return;
      }

      if (mode == WarehouseSyncMode.SharedFolder)
      {
        var dir = (settings.SharedWarehouseDirectory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dir))
        {
          WarehouseSharedFolderStore.StopWatcher();
          StopPolling();
          try { WarehouseSqliteStore.EnsureInitialized(); } catch { }
          _runtimeReady = true;
          _runtimeKey = "local";
          return;
        }

        try
        {
          WarehouseSharedFolderStore.EnsureInitialized(dir, seedFromLocalIfEmpty: true);
          HookSharedWatcher();
          StartSharedPolling(dir);
          _runtimeReady = true;
          _runtimeKey = key;
        }
        catch
        {
          WarehouseSharedFolderStore.StopWatcher();
          StopPolling();
          try { WarehouseSqliteStore.EnsureInitialized(); } catch { }
          _runtimeReady = true;
          _runtimeKey = "local-fallback";
        }
        return;
      }

      WarehouseSharedFolderStore.StopWatcher();
      StopPolling();
      try { WarehouseSqliteStore.EnsureInitialized(); } catch { }
      _runtimeReady = true;
      _runtimeKey = key;
    }
  }

  /// <summary>Erzwingt erneutes Anwenden (z. B. nach Speichern in Netzwerkeinstellungen).</summary>
  public static void ResetRuntimeMode()
  {
    lock (InitGate)
    {
      _runtimeReady = false;
      _runtimeKey = string.Empty;
      StopPolling();
      WarehouseHubServer.Stop();
      WarehouseSharedFolderStore.StopWatcher();
    }
  }

  public static void InitializeWithAllProfiles(int defaultOriginalQuantity = 0)
  {
    EnsureInitialized();
    var items = PipeStockCatalog.All
      .Select(profile => new PipeWarehouseStockItem
      {
        ProfileId = profile.Id,
        Material = profile.Material,
        LengthMm = CutOptimizationDefaults.StockLengthMm,
        Quantity = defaultOriginalQuantity
      })
      .ToList();
    RefreshDisplayNames(items);
    Save(items);
  }

  public static void RefreshDisplayNames(IEnumerable<PipeWarehouseStockItem> items)
  {
    foreach (var item in items)
    {
      var profile = PipeStockCatalog.TryGet(item.ProfileId);
      if (profile is not null)
        item.RefreshFromProfile(profile);
    }
  }

  public static string GetStatusHint() => GetMode() switch
  {
    WarehouseSyncMode.SharedFolder => " · gemeinsamer Lager-Ordner",
    WarehouseSyncMode.Host when WarehouseHubServer.IsRunning =>
      $" · Lager-Zentrale aktiv (Port {WarehouseHubServer.Port})",
    WarehouseSyncMode.Host => " · Lager-Zentrale (Start fehlgeschlagen – Port/Firewall?)",
    WarehouseSyncMode.Client => " · verbunden mit Lager-Zentrale",
    _ => string.Empty
  };

  public static IReadOnlyList<WarehousePresenceDto> GetOnlinePeers()
  {
    try
    {
      var mode = GetMode();
      if (mode == WarehouseSyncMode.Local)
        return [WarehousePresenceRegistry.CreateLocalPeer("local")];

      if (mode == WarehouseSyncMode.SharedFolder)
      {
        var dir = AppSettingsStore.Load().SharedWarehouseDirectory?.Trim();
        if (string.IsNullOrWhiteSpace(dir))
          return [WarehousePresenceRegistry.CreateLocalPeer("local")];

        var self = WarehousePresenceRegistry.CreateLocalPeer("shared");
        try { WarehouseSharedFolderStore.HeartbeatPresence(dir, self); } catch { }
        try { return WarehouseSharedFolderStore.GetActivePresence(dir); } catch { return [self]; }
      }

      if (mode == WarehouseSyncMode.Host)
      {
        WarehousePresenceRegistry.Heartbeat(WarehousePresenceRegistry.CreateLocalPeer("host"));
        return WarehousePresenceRegistry.GetActive();
      }

      var url = GetClientUrl();
      var peer = WarehousePresenceRegistry.CreateLocalPeer("client");
      return WarehouseHubClient.HeartbeatPresence(url, peer);
    }
    catch
    {
      return [];
    }
  }

  public static string FormatOnlineSummary(IReadOnlyList<WarehousePresenceDto> peers)
  {
    if (peers is null || peers.Count == 0)
      return "Netzwerk: niemand online";

    var mode = GetMode();
    if (mode == WarehouseSyncMode.Local)
      return "Lokal · " + peers[0].DisplayName;

    if (mode == WarehouseSyncMode.SharedFolder)
    {
      var others = peers
        .Where(p => !string.Equals(p.ClientId, MachineFingerprintService.GetMachineFingerprint(), StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (others.Count == 0)
        return "Lager-Ordner · nur dieser PC online";
      if (others.Count == 1)
        return "Lager-Ordner · verbunden: " + others[0].DisplayName;
      return $"Lager-Ordner · verbunden ({others.Count}): "
             + string.Join(", ", others.Select(p => p.DisplayName));
    }

    if (mode == WarehouseSyncMode.Host)
    {
      var clients = peers
        .Where(p => !string.Equals(p.Role, "host", StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (clients.Count == 0)
        return "Zentrale aktiv · warte auf andere PCs";
      if (clients.Count == 1)
        return "Verbunden: " + clients[0].DisplayName;
      return $"Verbunden ({clients.Count}): " + string.Join(", ", clients.Select(p => p.DisplayName));
    }

    if (peers.Count == 1)
      return "Online: " + peers[0].DisplayName;

    return $"Online ({peers.Count}): " + string.Join(", ", peers.Select(p => p.DisplayName));
  }

  public static string? ReadConfiguredNetworkDirectory()
  {
    var dir = AppSettingsStore.Load().SharedWarehouseDirectory;
    return string.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
  }

  public static void SetConfiguredNetworkDirectory(string? directory)
  {
    var settings = AppSettingsStore.Load();
    settings.SharedWarehouseDirectory = directory?.Trim() ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(settings.SharedWarehouseDirectory))
      settings.WarehouseSyncMode = nameof(WarehouseSyncMode.SharedFolder);
    AppSettingsStore.Save(settings);
    ResetRuntimeMode();
  }

  private static WarehouseSyncMode GetMode() =>
    ParseMode(AppSettingsStore.Load().WarehouseSyncMode);

  private static string GetSharedDirectory()
  {
    var dir = AppSettingsStore.Load().SharedWarehouseDirectory?.Trim();
    if (string.IsNullOrWhiteSpace(dir))
      throw new InvalidOperationException("Kein gemeinsamer Lager-Ordner konfiguriert.");
    return dir;
  }

  private static string GetClientUrl()
  {
    var url = WarehouseHubClient.NormalizeBaseUrl(AppSettingsStore.Load().WarehouseHubUrl);
    if (string.IsNullOrWhiteSpace(url))
      throw new InvalidOperationException("Keine Lager-Zentrale konfiguriert (Einstellungen).");
    return url;
  }

  private static WarehouseSyncMode ParseMode(string? raw)
  {
    if (Enum.TryParse<WarehouseSyncMode>(raw, true, out var mode))
      return mode;
    return WarehouseSyncMode.Local;
  }

  private static void HookSharedWatcher()
  {
    if (_sharedWatcherHooked)
      return;
    WarehouseSharedFolderStore.ExternalChanged += () =>
    {
      try
      {
        var version = WarehouseSharedFolderStore.GetCurrentVersion(GetSharedDirectory());
        if (version != _knownVersion)
        {
          _knownVersion = version;
          ExternalChanged?.Invoke();
        }
      }
      catch
      {
        ExternalChanged?.Invoke();
      }
    };
    _sharedWatcherHooked = true;
  }

  private static void StartSharedPolling(string directory)
  {
    StopPolling();
    _poller = new DispatcherPoller(TimeSpan.FromSeconds(2.5), () =>
    {
      // Nur Version prüfen – Presence macht die UI separat, ohne Init-Schleife.
      _ = Task.Run(() =>
      {
        try
        {
          var version = WarehouseSharedFolderStore.GetCurrentVersion(directory);
          if (version != _knownVersion)
          {
            _knownVersion = version;
            ExternalChanged?.Invoke();
          }
        }
        catch
        {
        }
      });
    });
    _poller.Start();
  }

  private static void StartHubPolling(string? hubUrl)
  {
    StopPolling();
    var url = WarehouseHubClient.NormalizeBaseUrl(hubUrl);
    if (string.IsNullOrWhiteSpace(url))
      return;

    _poller = new DispatcherPoller(TimeSpan.FromSeconds(2.5), () =>
    {
      _ = Task.Run(() =>
      {
        try
        {
          var (version, _) = WarehouseHubClient.Load(url);
          if (version != _knownVersion)
          {
            _knownVersion = version;
            ExternalChanged?.Invoke();
          }
        }
        catch
        {
        }
      });
    });
    _poller.Start();
  }

  private static void StopPolling()
  {
    _poller?.Dispose();
    _poller = null;
  }

  private sealed class DispatcherPoller : IDisposable
  {
    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _interval;
    private int _started;

    public DispatcherPoller(TimeSpan interval, Action tick)
    {
      _interval = interval;
      _timer = new System.Threading.Timer(
        _ =>
        {
          try { tick(); }
          catch { }
        },
        null,
        Timeout.Infinite,
        Timeout.Infinite);
    }

    public void Start()
    {
      if (Interlocked.Exchange(ref _started, 1) == 1)
        return;
      _timer.Change(_interval, _interval);
    }

    public void Dispose()
    {
      try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
      try { _timer.Dispose(); } catch { }
    }
  }
}