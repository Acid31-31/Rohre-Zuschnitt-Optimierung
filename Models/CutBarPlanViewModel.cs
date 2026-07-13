namespace RohreZuschnittOptimierung.Models;

public sealed class CutBarPlanViewModel
{
  public required CutBarPlan Bar { get; init; }
  public double StockLengthMm { get; init; }
  public double KerfMm { get; init; }

  public string Header =>
    $"{Bar.StockLabel} {Bar.BarNumber} · {FormatMm(Bar.StockLengthMm)} · genutzt {FormatMm(Bar.UsedMm)} · Rest {FormatMm(Bar.WasteMm)} · Säge {Bar.SawAdjustments}× verstellen";

  public string Details => Bar.SawPlanSummary;

  private static string FormatMm(double valueMm) =>
    Math.Abs(valueMm - Math.Round(valueMm)) < 0.01 ? $"{valueMm:0} mm" : $"{valueMm:0.##} mm";
}
