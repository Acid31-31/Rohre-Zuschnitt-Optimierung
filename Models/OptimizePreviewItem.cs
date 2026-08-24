using System.Globalization;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Models;

public sealed class OptimizePreviewItem
{
  public string DrawingName { get; init; } = "—";
  public string? PdfPath { get; init; }
  public double LengthMm { get; init; }
  public int Quantity { get; init; }
  public string MiterText { get; init; } = string.Empty;
  public bool IsTooLong { get; init; }
  public bool LengthMissing { get; init; }
  public string StatusText =>
    LengthMissing ? "Länge fehlt"
    : IsTooLong ? "Zu lang für Originalstange"
    : "OK";
  public string LengthText => LengthMm > 0
    ? LengthMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm"
    : "—";
}
