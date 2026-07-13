namespace RohreZuschnittOptimierung.Models;

public sealed class CutPieceInstance
{
  public double LengthMm { get; init; }
  public string? DrawingName { get; init; }
  public double MiterEnd1Deg { get; init; }
  public double MiterEnd2Deg { get; init; }

  public string MiterSummary => MiterNotation.Format(MiterEnd1Deg, MiterEnd2Deg);

  public IEnumerable<double> MiterAngles()
  {
    if (MiterEnd1Deg > 0.1)
      yield return MiterEnd1Deg;
    if (MiterEnd2Deg > 0.1)
      yield return MiterEnd2Deg;
  }

  public int MiterCutCount =>
    (MiterEnd1Deg > 0.1 ? 1 : 0) + (MiterEnd2Deg > 0.1 ? 1 : 0);
}

public sealed class CutBarPlan
{
  public int BarNumber { get; init; }
  public double StockLengthMm { get; init; }
  public bool IsRemnant { get; init; }
  public string StockLabel => IsRemnant ? "Rohrest" : "Originalstange";
  public IReadOnlyList<CutPieceInstance> Pieces { get; init; } = [];
  public IReadOnlyList<OrientedCutPiece> OrientedPieces { get; init; } = [];
  public IReadOnlyList<StockCutStep> StockCutSteps { get; init; } = [];
  public double UsedMm { get; init; }
  public double WasteMm { get; init; }
  public int SawAdjustments { get; init; }
  public int ExternalMiterOps { get; init; }
  public string SawPlanSummary { get; set; } = string.Empty;
}

public sealed class CutOptimizationResult
{
  public IReadOnlyList<CutBarPlan> Bars { get; init; } = [];
  public int TotalBars { get; init; }
  public double TotalWasteMm { get; init; }
  public double StockLengthMm { get; init; }
  public int RemnantBarsUsed { get; init; }
  public int NewOriginalBarsUsed { get; init; }
  public int OrderedNewBarsCount { get; init; }
  public double KerfMm { get; init; }
  public int SawAdjustments { get; init; }
  public string SawPlanSummary { get; set; } = string.Empty;
}
