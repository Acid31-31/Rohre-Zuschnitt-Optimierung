using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class SawSequenceService
{
  public static List<CutPieceInstance> OrderForMinimalSawChanges(IEnumerable<CutPieceInstance> pieces)
  {
    var singles = new List<CutPieceInstance>();
    var unequalTrapezoids = new List<CutPieceInstance>();
    var equalTrapezoids = new List<CutPieceInstance>();
    var rectangles = new List<CutPieceInstance>();

    foreach (var piece in pieces)
    {
      if (MiterProfileService.TryGetSingleMiterAngle(piece, out _))
        singles.Add(piece);
      else if (MiterProfileService.TryGetUnequalTrapezoidAngles(piece, out _, out _))
        unequalTrapezoids.Add(piece);
      else if (MiterProfileService.TryGetBothEqualMiterAngles(piece, out _))
        equalTrapezoids.Add(piece);
      else
        rectangles.Add(piece);
    }

    var ordered = new List<CutPieceInstance>();

    foreach (var group in singles.GroupBy(MiterProfileService.GetProfileKey).OrderBy(entry => entry.Key))
      ordered.AddRange(group.OrderByDescending(piece => piece.LengthMm));

    foreach (var group in unequalTrapezoids
               .GroupBy(MiterProfileService.GetProfileKey)
               .OrderBy(entry => entry.Key))
      ordered.AddRange(group.OrderByDescending(piece => piece.LengthMm));

    ordered.AddRange(equalTrapezoids.OrderByDescending(piece => piece.LengthMm));
    ordered.AddRange(rectangles.OrderByDescending(piece => piece.LengthMm));

    return ordered;
  }

  public static string BuildBarSawPlan(CutBarPlan bar)
  {
    var sb = new System.Text.StringBuilder();

    if (bar.StockCutSteps.Count > 0)
    {
      sb.AppendLine($"Schnittfolge an der Stange ({bar.StockCutSteps.Count} Schnitte, Säge {bar.SawAdjustments}× verstellen):");
      foreach (var step in bar.StockCutSteps)
        sb.AppendLine($"  {step.StepNumber}. {step.SawAngleDeg:0}° – {step.Description}");
    }

    if (bar.ExternalMiterOps > 0)
    {
      sb.AppendLine(
        $"Zusätzlich: {bar.ExternalMiterOps} Gehrung(en) an Teilstößen mit unterschiedlichen Winkeln (separat an der Säge).");
    }
    else if (bar.OrientedPieces.Any(p => p.MiterCutCount > 0))
    {
      sb.AppendLine("Alle passenden Gehrungen sind in der Stangen-Schnittfolge enthalten.");
    }

    return sb.ToString().TrimEnd();
  }

  public static string BuildTotalSawPlan(CutOptimizationResult result)
  {
    var totalAdjustments = result.Bars.Sum(bar => bar.SawAdjustments);
    var totalExternal = result.Bars.Sum(bar => bar.ExternalMiterOps);

    if (totalAdjustments == 0 && totalExternal == 0)
      return "Sägeverstellung gesamt: 0× (nur lotrechte Schnitte)";

    var sb = new System.Text.StringBuilder();
    sb.Append($"Sägeverstellung an Stange: {totalAdjustments}×");
    if (totalExternal > 0)
      sb.Append($" · zusätzliche Gehrungen an Teilstößen: {totalExternal}×");
    if (result.RemnantBarsUsed > 0)
      sb.Append($" · {result.RemnantBarsUsed} Rohrest(e), {result.NewOriginalBarsUsed} Original neu");
    sb.Append(" · Rohreste zuerst, dann Original");

    return sb.ToString();
  }
}
