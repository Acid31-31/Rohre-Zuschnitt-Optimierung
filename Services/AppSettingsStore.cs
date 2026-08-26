using System.Globalization;
using System.IO;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class AppSettingsStore
{
  private const string FileName = "app-settings.xml";

  public static string FilePath =>
    Path.Combine(AppInfo.UserDataDirectory, FileName);

  public static AppSettings Load()
  {
    if (!File.Exists(FilePath))
      return new AppSettings();

    try
    {
      var root = XDocument.Load(FilePath).Root;
      if (root is null)
        return new AppSettings();

      var localAi = root.Element(nameof(AppSettings.LocalAiEnabled)) is null
        ? true
        : ReadBool(root, nameof(AppSettings.LocalAiEnabled), true);

      return new AppSettings
      {
        StockLengthMm = ReadDouble(root, nameof(AppSettings.StockLengthMm), RohreZuschnittOptimierung.CutOptimizationDefaults.StockLengthMm),
        KerfMm = ReadDouble(root, nameof(AppSettings.KerfMm), RohreZuschnittOptimierung.CutOptimizationDefaults.KerfMm),
        LocalAiEnabled = localAi,
        OllamaBaseUrl = ReadString(root, nameof(AppSettings.OllamaBaseUrl), "http://127.0.0.1:11435"),
        OllamaVisionModel = ReadString(root, nameof(AppSettings.OllamaVisionModel), BundledAiRuntime.DefaultModel),
        WarehouseSyncMode = ReadString(root, nameof(AppSettings.WarehouseSyncMode), nameof(WarehouseSyncMode.Local)),
        WarehouseHubPort = ReadInt(root, nameof(AppSettings.WarehouseHubPort), 5088),
        WarehouseHubUrl = ReadString(root, nameof(AppSettings.WarehouseHubUrl), string.Empty)
      };
    }
    catch
    {
      return new AppSettings();
    }
  }

  public static void Save(AppSettings settings)
  {
    var directory = Path.GetDirectoryName(FilePath)!;
    Directory.CreateDirectory(directory);

    if (string.IsNullOrWhiteSpace(settings.OllamaBaseUrl))
      settings.OllamaBaseUrl = "http://127.0.0.1:11435";
    if (string.IsNullOrWhiteSpace(settings.OllamaVisionModel))
      settings.OllamaVisionModel = BundledAiRuntime.DefaultModel;
    if (settings.WarehouseHubPort is < 1 or > 65535)
      settings.WarehouseHubPort = 5088;
    if (string.IsNullOrWhiteSpace(settings.WarehouseSyncMode))
      settings.WarehouseSyncMode = nameof(WarehouseSyncMode.Local);

    var root = new XElement(
      "AppSettings",
      new XElement(
        nameof(AppSettings.StockLengthMm),
        settings.StockLengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XElement(
        nameof(AppSettings.KerfMm),
        settings.KerfMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XElement(nameof(AppSettings.LocalAiEnabled), settings.LocalAiEnabled ? "true" : "false"),
      new XElement(nameof(AppSettings.OllamaBaseUrl), settings.OllamaBaseUrl),
      new XElement(nameof(AppSettings.OllamaVisionModel), settings.OllamaVisionModel),
      new XElement(nameof(AppSettings.WarehouseSyncMode), settings.WarehouseSyncMode),
      new XElement(nameof(AppSettings.WarehouseHubPort), settings.WarehouseHubPort.ToString(CultureInfo.InvariantCulture)),
      new XElement(nameof(AppSettings.WarehouseHubUrl), settings.WarehouseHubUrl ?? string.Empty));

    root.Save(FilePath);
  }

  private static double ReadDouble(XElement root, string name, double fallback) =>
    double.TryParse((string?)root.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
      ? value
      : fallback;

  private static int ReadInt(XElement root, string name, int fallback) =>
    int.TryParse((string?)root.Element(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
      ? value
      : fallback;

  private static bool ReadBool(XElement root, string name, bool fallback)
  {
    var raw = (string?)root.Element(name);
    if (string.IsNullOrWhiteSpace(raw))
      return fallback;
    return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("1", StringComparison.OrdinalIgnoreCase);
  }

  private static string ReadString(XElement root, string name, string fallback)
  {
    var raw = ((string?)root.Element(name))?.Trim();
    return string.IsNullOrWhiteSpace(raw) ? fallback : raw;
  }
}
