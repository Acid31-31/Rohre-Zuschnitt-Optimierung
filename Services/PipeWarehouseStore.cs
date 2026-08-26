using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Fassade: lokal / Host / Client. Kein gemeinsames Datei-Lager mehr.
/// </summary>
public static class PipeWarehouseStore
{
  private static long _knownVersion = 1;
  private static DispatcherPoller? _poller;

  /// <summary>Andere PCs haben das Lager geändert (Polling gegen Zentrale).</summary>
  public static event Action? ExternalChanged;

  public static string FilePath => WarehouseSqliteStore.DatabasePath;

  public static bool UsesSharedNetworkPath => GetMode() == WarehouseSyncMode.Client;

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

    var local = WarehouseSqliteStore.Load();
    _knownVersion = local.Version;
    return local.Items;
  }

  public static void Save(IEnumerable<PipeWarehouseStockItem> items)
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
    if (mode == WarehouseSyncMode.Host)
    {
      WarehouseSqliteStore.EnsureInitialized();
      WarehouseHubServer.Start(settings.WarehouseHubPort > 0 ? settings.WarehouseHubPort : 5088);
      StopPolling();
    }
    else
    {
      WarehouseHubServer.Stop();
      if (mode == WarehouseSyncMode.Client)
      {
        StartPolling(settings.WarehouseHubUrl);
      }
      else
      {
        StopPolling();
        WarehouseSqliteStore.EnsureInitialized();
      }
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

  public static string GetStatusHint()
  {
    return GetMode() switch
    {
      WarehouseSyncMode.Host when WarehouseHubServer.IsRunning =>
        $" · Lager-Zentrale aktiv (Port {WarehouseHubServer.Port})",
      WarehouseSyncMode.Host => " · Lager-Zentrale (Start fehlgeschlagen – Port/Firewall?)",
      WarehouseSyncMode.Client => " · verbunden mit Lager-Zentrale",
      _ => string.Empty
    };
  }

  // Compatibility no-ops for removed network-share API
  public static string? ReadConfiguredNetworkDirectory() => null;

  public static void SetConfiguredNetworkDirectory(string? directory)
  {
    // entfernt – Netzwerkordner-Freigabe absichtlich nicht mehr unterstützt
  }

  private static WarehouseSyncMode GetMode() =>
    ParseMode(AppSettingsStore.Load().WarehouseSyncMode);

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

  private static void StartPolling(string? hubUrl)
  {
    StopPolling();
    var url = WarehouseHubClient.NormalizeBaseUrl(hubUrl);
    if (string.IsNullOrWhiteSpace(url))
      return;

    _poller = new DispatcherPoller(TimeSpan.FromSeconds(2.5), () =>
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
    _poller.Start();
  }

  private static void StopPolling()
  {
    _poller?.Dispose();
    _poller = null;
  }

  private sealed class DispatcherPoller : IDisposable
  {
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public DispatcherPoller(TimeSpan interval, Action tick)
    {
      _timer = new System.Windows.Threading.DispatcherTimer
      {
        Interval = interval
      };
      _timer.Tick += (_, _) => tick();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
      _timer.Stop();
    }
  }
}
