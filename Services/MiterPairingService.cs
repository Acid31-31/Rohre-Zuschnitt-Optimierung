using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class MiterPairingService
{
  public static List<OrientedCutPiece> OrientPiecesOnBar(IReadOnlyList<CutPieceInstance> pieces)
  {
    if (pieces.Count == 0)
      return [];

    var result = new List<OrientedCutPiece>();
    var index = 0;

    while (index < pieces.Count)
    {
      var current = pieces[index];

      if (index + 1 < pieces.Count
          && TryGetSingleMiterAngle(current, out var singleLeft)
          && TryGetSingleMiterAngle(pieces[index + 1], out var singleRight)
          && Math.Abs(singleLeft - singleRight) < 0.1)
      {
        result.Add(OrientSingleMiter(current, miterOnRight: true));
        result.Add(OrientSingleMiter(pieces[index + 1], miterOnRight: false));
        index += 2;
        continue;
      }

      if (index + 1 < pieces.Count
          && TryGetUnequalTrapezoidAngles(current, out var trapA1, out var trapA2)
          && TryGetUnequalTrapezoidAngles(pieces[index + 1], out var trapB1, out var trapB2)
          && AnglesMatch(trapA1, trapB1)
          && AnglesMatch(trapA2, trapB2))
      {
        result.Add(OrientTrapezoid(current, flipped: false));
        result.Add(OrientTrapezoid(pieces[index + 1], flipped: true));
        index += 2;
        continue;
      }

      if (TryGetUnequalTrapezoidAngles(current, out var leftAngle, out var rightAngle))
      {
        var prevRight = result.Count > 0 ? result[^1].MiterRightDeg : (double?)null;
        var next = index + 1 < pieces.Count ? pieces[index + 1] : null;
        result.Add(OrientTrapezoidBest(current, leftAngle, rightAngle, prevRight, next));
        index++;
        continue;
      }

      if (TryGetBothEqualMiterAngles(current, out var equalAngle))
      {
        var prevRight = result.Count > 0 ? result[^1].MiterRightDeg : (double?)null;
        var flip = prevRight is > 0.1 && Math.Abs(prevRight.Value - equalAngle) < 0.1;
        result.Add(OrientTrapezoid(current, flipped: flip));
        index++;
        continue;
      }

      if (TryGetSingleMiterAngle(current, out var singleAngle))
      {
        var prevRight = result.Count > 0 ? result[^1].MiterRightDeg : 0;
        var miterOnRight = prevRight > 0.1 && Math.Abs(prevRight - singleAngle) < 0.1
          ? false
          : result.Count % 2 == 0;
        result.Add(OrientSingleMiter(current, miterOnRight));
        index++;
        continue;
      }

      result.Add(OrientRectangle(current));
      index++;
    }

    return result;
  }

  public static IReadOnlyList<double> BuildCutAnglesOnBar(IReadOnlyList<OrientedCutPiece> pieces)
  {
    if (pieces.Count == 0)
      return [];

    var cuts = new List<double>
    {
      pieces[0].MiterLeftDeg > 0.1 ? pieces[0].MiterLeftDeg : 90
    };

    for (var i = 0; i < pieces.Count - 1; i++)
      cuts.Add(GetSharedJunctionAngle(pieces[i], pieces[i + 1]));

    cuts.Add(pieces[^1].MiterRightDeg > 0.1 ? pieces[^1].MiterRightDeg : 90);
    return cuts;
  }

  public static IReadOnlyList<StockCutStep> BuildStockCutSteps(IReadOnlyList<OrientedCutPiece> pieces)
  {
    var angles = BuildCutAnglesOnBar(pieces);
    if (angles.Count == 0)
      return [];

    var steps = new List<StockCutStep>();
    for (var i = 0; i < angles.Count; i++)
    {
      var angle = angles[i];
      var description = i switch
      {
        0 => angle > 0.1 && Math.Abs(angle - 90) > 0.1
          ? $"Schnitt am Stangenanfang ({angle:0}°)"
          : "Schnitt am Stangenanfang",
        _ when i == angles.Count - 1 => angle > 0.1 && Math.Abs(angle - 90) > 0.1
          ? $"Schnitt vor Verschnitt / Stangenende ({angle:0}°)"
          : "Schnitt vor Verschnitt / Stangenende",
        _ when IsSharedJunctionCut(pieces, angles, i) =>
          $"Gemeinsamer {angle:0}°-Schnitt zwischen zwei Teilen (beide Enden in einem Schnitt)",
        _ when i > 0 && i < pieces.Count =>
          BuildSeparateJunctionDescription(pieces[i - 1], pieces[i]),
        _ => $"Trennschnitt ({angle:0}°)"
      };

      steps.Add(new StockCutStep
      {
        StepNumber = i + 1,
        SawAngleDeg = angle,
        Description = description
      });
    }

    return steps;
  }

  public static int CountSawAdjustmentsOnBar(IReadOnlyList<double> cutAngles)
  {
    if (cutAngles.Count <= 1)
      return 0;

    var changes = 0;
    for (var i = 1; i < cutAngles.Count; i++)
    {
      if (Math.Abs(cutAngles[i] - cutAngles[i - 1]) > 0.1)
        changes++;
    }

    return changes;
  }

  public static bool IsSharedJunctionCut(
    IReadOnlyList<OrientedCutPiece> pieces,
    IReadOnlyList<double> cutAngles,
    int cutIndex)
  {
    if (cutIndex <= 0 || cutIndex >= cutAngles.Count || cutIndex >= pieces.Count)
      return false;

    var angle = cutAngles[cutIndex];
    if (Math.Abs(angle - 90) < 0.1)
      return false;

    var left = pieces[cutIndex - 1];
    var right = pieces[cutIndex];
    return left.MiterRightDeg > 0.1
           && right.MiterLeftDeg > 0.1
           && Math.Abs(left.MiterRightDeg - right.MiterLeftDeg) < 0.1
           && Math.Abs(left.MiterRightDeg - angle) < 0.1;
  }

  public static int CountExternalMiterOps(IReadOnlyList<OrientedCutPiece> pieces, IReadOnlyList<double> cutAngles)
  {
    if (pieces.Count == 0 || cutAngles.Count == 0)
      return 0;

    var count = 0;
    for (var i = 0; i < pieces.Count - 1; i++)
    {
      if (IsSharedJunctionCut(pieces, cutAngles, i + 1))
        continue;

      if (pieces[i].MiterRightDeg > 0.1)
        count++;
      if (pieces[i + 1].MiterLeftDeg > 0.1)
        count++;
    }

    return count;
  }

  private static string BuildSeparateJunctionDescription(OrientedCutPiece left, OrientedCutPiece right)
  {
    var leftText = left.MiterRightDeg > 0.1 ? $"{left.MiterRightDeg:0}°" : "lotrecht";
    var rightText = right.MiterLeftDeg > 0.1 ? $"{right.MiterLeftDeg:0}°" : "lotrecht";
    return $"Lotrechter Trennschnitt – Gehrungen separat an Teilen ({leftText} / {rightText})";
  }

  private static double GetSharedJunctionAngle(OrientedCutPiece left, OrientedCutPiece right)
  {
    if (left.MiterRightDeg > 0.1
        && right.MiterLeftDeg > 0.1
        && Math.Abs(left.MiterRightDeg - right.MiterLeftDeg) < 0.1)
      return left.MiterRightDeg;

    return 90;
  }

  private static OrientedCutPiece OrientTrapezoidBest(
    CutPieceInstance piece,
    double angleA,
    double angleB,
    double? previousRightMiter,
    CutPieceInstance? nextPiece)
  {
    var optionNormal = OrientTrapezoid(piece, flipped: false);
    var optionFlipped = OrientTrapezoid(piece, flipped: true);
    return ScoreTrapezoidOrientation(optionNormal, previousRightMiter, nextPiece) >=
           ScoreTrapezoidOrientation(optionFlipped, previousRightMiter, nextPiece)
      ? optionNormal
      : optionFlipped;
  }

  private static int ScoreTrapezoidOrientation(
    OrientedCutPiece oriented,
    double? previousRightMiter,
    CutPieceInstance? nextPiece)
  {
    var score = 0;

    if (previousRightMiter is > 0.1 && Math.Abs(previousRightMiter.Value - oriented.MiterLeftDeg) < 0.1)
      score += 20;

    if (nextPiece is null)
      return score;

    if (TryGetSingleMiterAngle(nextPiece, out var nextSingle)
        && Math.Abs(nextSingle - oriented.MiterRightDeg) < 0.1)
      score += 20;

    if (TryGetUnequalTrapezoidAngles(nextPiece, out var nextA, out var nextB))
    {
      if (Math.Abs(nextA - oriented.MiterRightDeg) < 0.1)
        score += 15;
      if (Math.Abs(nextB - oriented.MiterRightDeg) < 0.1)
        score += 10;
    }

    if (TryGetBothEqualMiterAngles(nextPiece, out var nextEqual)
        && Math.Abs(nextEqual - oriented.MiterRightDeg) < 0.1)
      score += 15;

    return score;
  }

  private static OrientedCutPiece OrientSingleMiter(CutPieceInstance piece, bool miterOnRight)
  {
    var angle = Math.Max(piece.MiterEnd1Deg, piece.MiterEnd2Deg);
    return new OrientedCutPiece
    {
      Source = piece,
      MiterLeftDeg = miterOnRight ? 0 : angle,
      MiterRightDeg = miterOnRight ? angle : 0,
      WasFlipped = !miterOnRight
    };
  }

  private static OrientedCutPiece OrientTrapezoid(CutPieceInstance piece, bool flipped) =>
    new()
    {
      Source = piece,
      MiterLeftDeg = flipped ? piece.MiterEnd2Deg : piece.MiterEnd1Deg,
      MiterRightDeg = flipped ? piece.MiterEnd1Deg : piece.MiterEnd2Deg,
      WasFlipped = flipped
    };

  private static OrientedCutPiece OrientRectangle(CutPieceInstance piece) =>
    new()
    {
      Source = piece,
      MiterLeftDeg = 0,
      MiterRightDeg = 0,
      WasFlipped = false
    };

  private static bool TryGetSingleMiterAngle(CutPieceInstance piece, out double angle)
  {
    var hasLeft = piece.MiterEnd1Deg > 0.1;
    var hasRight = piece.MiterEnd2Deg > 0.1;

    if (hasLeft && hasRight)
    {
      angle = 0;
      return false;
    }

    if (!hasLeft && !hasRight)
    {
      angle = 0;
      return false;
    }

    angle = hasLeft ? piece.MiterEnd1Deg : piece.MiterEnd2Deg;
    return true;
  }

  private static bool TryGetBothEqualMiterAngles(CutPieceInstance piece, out double angle)
  {
    angle = 0;
    if (piece.MiterEnd1Deg <= 0.1 || piece.MiterEnd2Deg <= 0.1)
      return false;

    if (Math.Abs(piece.MiterEnd1Deg - piece.MiterEnd2Deg) > 0.1)
      return false;

    angle = piece.MiterEnd1Deg;
    return true;
  }

  private static bool TryGetUnequalTrapezoidAngles(CutPieceInstance piece, out double angleA, out double angleB)
  {
    angleA = piece.MiterEnd1Deg;
    angleB = piece.MiterEnd2Deg;

    if (angleA <= 0.1 || angleB <= 0.1)
      return false;

    return Math.Abs(angleA - angleB) > 0.1;
  }

  private static bool AnglesMatch(double left, double right) =>
    Math.Abs(left - right) < 0.1;
}
