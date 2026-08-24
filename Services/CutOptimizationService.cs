using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class CutOptimizationService
{
  public static CutOptimizationResult Optimize(
    double originalStockLengthMm,
    double kerfMm,
    IEnumerable<CutPartEntry> parts,
    IEnumerable<StockRemnantEntry>? remnants = null)
  {
    if (originalStockLengthMm <= 0)
      throw new ArgumentOutOfRangeException(nameof(originalStockLengthMm), "Stangenlänge muss größer als 0 sein.");

    if (kerfMm < 0)
      throw new ArgumentOutOfRangeException(nameof(kerfMm), "Schnittbreite darf nicht negativ sein.");

    var pieces = parts
      .Where(part => part.Quantity > 0 && part.LengthMm > 0)
      .SelectMany(part => Enumerable.Repeat(new CutPieceInstance
      {
        LengthMm = part.LengthMm,
        DrawingName = part.DrawingName,
        MiterEnd1Deg = part.MiterEnd1Deg,
        MiterEnd2Deg = part.MiterEnd2Deg
      }, part.Quantity))
      .OrderBy(MiterProfileService.GetProfileSortOrder)
      .ThenBy(MiterProfileService.GetProfileKey)
      .ThenByDescending(piece => piece.LengthMm)
      .ToList();

    if (pieces.Count == 0)
      throw new InvalidOperationException("Keine Teile zum Optimieren vorhanden.");

    if (pieces.Any(piece => piece.LengthMm > originalStockLengthMm))
    {
      var oversized = pieces
        .Where(piece => piece.LengthMm > originalStockLengthMm)
        .GroupBy(piece => (piece.DrawingName ?? "Teil", piece.LengthMm))
        .Select(group =>
          "• " + (string.IsNullOrWhiteSpace(group.Key.Item1) ? "Teil" : group.Key.Item1)
          + ": " + group.Key.LengthMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
          + " mm (" + group.Count() + "×)")
        .ToList();

      throw new InvalidOperationException(
        "Mindestens ein Teil ist länger als die Originalstange ("
        + originalStockLengthMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        + " mm):"
        + Environment.NewLine
        + string.Join(Environment.NewLine, oversized));
    }

    var bars = CreateInitialBars(remnants);
    var orderedNewBars = 0;

    foreach (var piece in pieces)
    {
      var bestIndex = FindBestFitBar(bars, piece, kerfMm, remnantsOnly: true);
      if (bestIndex < 0)
        bestIndex = FindBestFitBar(bars, piece, kerfMm, remnantsOnly: false);

      if (bestIndex >= 0)
        bars[bestIndex].Pieces.Add(piece);
      else
      {
        orderedNewBars++;
        bars.Add(CreateOriginalBar(originalStockLengthMm, piece));
      }
    }

    var plans = bars
      .Where(bar => bar.Pieces.Count > 0)
      .Select((bar, index) => BuildBarPlan(bar, index + 1, kerfMm))
      .ToList();

    var result = new CutOptimizationResult
    {
      Bars = plans,
      TotalBars = plans.Count,
      TotalWasteMm = plans.Sum(plan => plan.WasteMm),
      StockLengthMm = originalStockLengthMm,
      RemnantBarsUsed = plans.Count(plan => plan.IsRemnant),
      NewOriginalBarsUsed = plans.Count(plan => !plan.IsRemnant),
      OrderedNewBarsCount = orderedNewBars,
      KerfMm = kerfMm,
      SawAdjustments = plans.Sum(plan => plan.SawAdjustments)
    };
    result.SawPlanSummary = SawSequenceService.BuildTotalSawPlan(result);
    return result;
  }

  public static double CalculateUsedLength(IReadOnlyList<double> pieceLengthsMm, double kerfMm)
  {
    if (pieceLengthsMm.Count == 0)
      return 0;

    var pieceSum = pieceLengthsMm.Sum();
    var kerfCount = Math.Max(0, pieceLengthsMm.Count - 1);
    return pieceSum + kerfCount * kerfMm;
  }

  private static List<MutableBar> CreateInitialBars(IEnumerable<StockRemnantEntry>? remnants)
  {
    var bars = new List<MutableBar>();

    foreach (var remnant in (remnants ?? []).Where(r => r.LengthMm > 0 && r.Quantity > 0))
    {
      for (var i = 0; i < remnant.Quantity; i++)
      {
        bars.Add(new MutableBar
        {
          StockLengthMm = remnant.LengthMm,
          IsRemnant = !remnant.IsFullBar
        });
      }
    }

    return bars
      .OrderBy(bar => bar.IsRemnant ? 0 : 1)
      .ThenBy(bar => bar.StockLengthMm)
      .ToList();
  }

  private static MutableBar CreateOriginalBar(double stockLengthMm, CutPieceInstance firstPiece) =>
    new()
    {
      StockLengthMm = stockLengthMm,
      IsRemnant = false,
      Pieces = [firstPiece]
    };

  private static CutBarPlan BuildBarPlan(MutableBar bar, int barNumber, double kerfMm)
  {
    var ordered = SawSequenceService.OrderForMinimalSawChanges(bar.Pieces);
    var oriented = MiterPairingService.OrientPiecesOnBar(ordered);
    var cutAngles = MiterPairingService.BuildCutAnglesOnBar(oriented);
    var used = CalculateUsedLength(ordered.Select(p => p.LengthMm).ToList(), kerfMm);

    var plan = new CutBarPlan
    {
      BarNumber = barNumber,
      StockLengthMm = bar.StockLengthMm,
      IsRemnant = bar.IsRemnant,
      Pieces = ordered,
      OrientedPieces = oriented,
      StockCutSteps = MiterPairingService.BuildStockCutSteps(oriented),
      UsedMm = used,
      WasteMm = bar.StockLengthMm - used,
      SawAdjustments = MiterPairingService.CountSawAdjustmentsOnBar(cutAngles),
      ExternalMiterOps = MiterPairingService.CountExternalMiterOps(oriented, cutAngles)
    };

    plan.SawPlanSummary = SawSequenceService.BuildBarSawPlan(plan);
    return plan;
  }

  private static int FindBestFitBar(
    IReadOnlyList<MutableBar> bars,
    CutPieceInstance piece,
    double kerfMm,
    bool remnantsOnly)
  {
    var bestBarIndex = -1;
    var bestRemaining = double.MaxValue;
    var bestCompatibility = int.MinValue;

    for (var i = 0; i < bars.Count; i++)
    {
      var bar = bars[i];
      if (remnantsOnly && !bar.IsRemnant)
        continue;

      if (!remnantsOnly && bar.IsRemnant)
        continue;

      if (!CanFit(bar.Pieces, piece.LengthMm, bar.StockLengthMm, kerfMm))
        continue;

      var usedAfter = bar.Pieces.Sum(p => p.LengthMm) + piece.LengthMm + kerfMm * bar.Pieces.Count;
      var remaining = bar.StockLengthMm - usedAfter;
      var compatibility = MiterProfileService.ScoreBarCompatibility(bar.Pieces, piece);

      if (remaining < bestRemaining - 0.01
          || (Math.Abs(remaining - bestRemaining) < 0.01 && compatibility > bestCompatibility))
      {
        bestRemaining = remaining;
        bestCompatibility = compatibility;
        bestBarIndex = i;
      }
    }

    return bestBarIndex;
  }

  private static bool CanFit(
    IReadOnlyList<CutPieceInstance> bar,
    double pieceLengthMm,
    double stockLengthMm,
    double kerfMm)
  {
    var usedAfter = bar.Sum(p => p.LengthMm) + pieceLengthMm + kerfMm * bar.Count;
    return usedAfter <= stockLengthMm + 0.0001;
  }

  private sealed class MutableBar
  {
    public double StockLengthMm { get; init; }
    public bool IsRemnant { get; init; }
    public List<CutPieceInstance> Pieces { get; init; } = [];
  }
}
