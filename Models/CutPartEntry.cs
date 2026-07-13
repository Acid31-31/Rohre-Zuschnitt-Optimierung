namespace RohreZuschnittOptimierung.Models;

public sealed class CutPartEntry
{
  public string? DrawingName { get; set; }
  public string? PdfPath { get; set; }
  public double LengthMm { get; set; }
  public double MiterEnd1Deg { get; set; }
  public double MiterEnd2Deg { get; set; }
  public int Quantity { get; set; } = 1;

  public string MiterSummary => MiterNotation.Format(MiterEnd1Deg, MiterEnd2Deg);

  public bool HasBothMiters => MiterEnd1Deg > 0.1 && MiterEnd2Deg > 0.1;

  public bool HasSingleMiter =>
    (MiterEnd1Deg > 0.1 && MiterEnd2Deg <= 0.1) || (MiterEnd2Deg > 0.1 && MiterEnd1Deg <= 0.1);
}

public static class MiterNotation
{
  public static string Format(double end1Deg, double end2Deg)
  {
    var e1 = end1Deg < 0.1 ? "0°" : $"{end1Deg:0}°";
    var e2 = end2Deg < 0.1 ? "0°" : $"{end2Deg:0}°";

    if (end1Deg <= 0.1 && end2Deg <= 0.1)
      return "lotrecht";

    if (HasSingleMiter(end1Deg, end2Deg))
      return $"{Math.Max(end1Deg, end2Deg):0}° (eine Seite, drehbar)";

    return $"{e1} / {e2}";
  }

  public static bool HasSingleMiter(double end1Deg, double end2Deg) =>
    (end1Deg > 0.1 && end2Deg <= 0.1) || (end2Deg > 0.1 && end1Deg <= 0.1);

  public static bool HasBothMiters(double end1Deg, double end2Deg) =>
    end1Deg > 0.1 && end2Deg > 0.1;

  /// <summary>0° und 90° = lotrecht. Gehrung nur 0,1° … 89,9°.</summary>
  public static double NormalizeInputAngle(double angleDeg)
  {
    if (angleDeg < 0)
      throw new InvalidOperationException("Gehrung darf nicht negativ sein.");

    if (angleDeg > 90.01)
      throw new InvalidOperationException("Gehrung muss zwischen 0° und 90° liegen (90° = lotrecht).");

    if (angleDeg >= 89.95)
      return 0;

    return angleDeg;
  }
}
