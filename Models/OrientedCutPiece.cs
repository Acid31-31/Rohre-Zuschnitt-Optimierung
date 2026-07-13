namespace RohreZuschnittOptimierung.Models;

/// <summary>
/// Teil auf der Stange mit Ausrichtung (drehbar), damit Gehrungen sich an einer Schnittstelle teilen können.
/// </summary>
public sealed class OrientedCutPiece
{
  public required CutPieceInstance Source { get; init; }
  public double MiterLeftDeg { get; init; }
  public double MiterRightDeg { get; init; }
  public bool WasFlipped { get; init; }

  public double LengthMm => Source.LengthMm;
  public string? DrawingName => Source.DrawingName;

  public string MiterSummary => MiterNotation.Format(MiterLeftDeg, MiterRightDeg);

  public int MiterCutCount =>
    (MiterLeftDeg > 0.1 ? 1 : 0) + (MiterRightDeg > 0.1 ? 1 : 0);
}

public sealed class StockCutStep
{
  public int StepNumber { get; init; }
  public double SawAngleDeg { get; init; }
  public string Description { get; init; } = string.Empty;
}
