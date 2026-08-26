namespace RohreZuschnittOptimierung.Models;

public sealed class AppSettings
{
  public double StockLengthMm { get; set; } = RohreZuschnittOptimierung.CutOptimizationDefaults.StockLengthMm;

  public double KerfMm { get; set; } = RohreZuschnittOptimierung.CutOptimizationDefaults.KerfMm;

  /// <summary>Mitgelieferte lokale Vision-KI (AI\ im Programmordner) für Länge/Gehrung.</summary>
  public bool LocalAiEnabled { get; set; } = true;

  public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11435";

  public string OllamaVisionModel { get; set; } = "moondream";

  /// <summary>Local | Host | Client</summary>
  public string WarehouseSyncMode { get; set; } = nameof(Models.WarehouseSyncMode.Local);

  public int WarehouseHubPort { get; set; } = 5088;

  /// <summary>z. B. http://192.168.1.10:5088 – nur im Client-Modus.</summary>
  public string WarehouseHubUrl { get; set; } = string.Empty;
}
