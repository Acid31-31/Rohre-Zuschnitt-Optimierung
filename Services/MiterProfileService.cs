using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class MiterProfileService
{
  public static string GetProfileKey(CutPieceInstance piece)
  {
    if (TryGetSingleMiterAngle(piece, out var single))
      return $"single:{single:0.##}";

    if (TryGetUnequalTrapezoidAngles(piece, out var a, out var b))
    {
      var min = Math.Min(a, b);
      var max = Math.Max(a, b);
      return $"trap:{min:0.##}/{max:0.##}:L{piece.LengthMm:0.##}";
    }

    if (TryGetBothEqualMiterAngles(piece, out var equal))
      return $"equal:{equal:0.##}";

    return "rect";
  }

  public static int GetProfileSortOrder(CutPieceInstance piece)
  {
    if (TryGetSingleMiterAngle(piece, out var single))
      return 100 + (int)Math.Round(single);

    if (TryGetUnequalTrapezoidAngles(piece, out var a, out var b))
      return 300 + (int)Math.Round(Math.Min(a, b)) * 10 + (int)Math.Round(Math.Max(a, b));

    if (TryGetBothEqualMiterAngles(piece, out var equal))
      return 200 + (int)Math.Round(equal);

    return 900;
  }

  public static bool CanPotentiallyShareCut(CutPieceInstance left, CutPieceInstance right)
  {
    if (TryGetSingleMiterAngle(left, out var leftSingle) && TryGetSingleMiterAngle(right, out var rightSingle))
      return Math.Abs(leftSingle - rightSingle) < 0.1;

    if (TryGetUnequalTrapezoidAngles(left, out var la, out var lb)
        && TryGetUnequalTrapezoidAngles(right, out var ra, out var rb))
      return AnglesMatch(la, ra) && AnglesMatch(lb, rb);

    if (TryGetSingleMiterAngle(left, out var singleLeft) && TryGetUnequalTrapezoidAngles(right, out var rt1, out var rt2))
      return Math.Abs(singleLeft - rt1) < 0.1 || Math.Abs(singleLeft - rt2) < 0.1;

    if (TryGetSingleMiterAngle(right, out var singleRight) && TryGetUnequalTrapezoidAngles(left, out var lt1, out var lt2))
      return Math.Abs(singleRight - lt1) < 0.1 || Math.Abs(singleRight - lt2) < 0.1;

    if (TryGetBothEqualMiterAngles(left, out var equalLeft) && TryGetBothEqualMiterAngles(right, out var equalRight))
      return Math.Abs(equalLeft - equalRight) < 0.1;

    return false;
  }

  public static int ScoreBarCompatibility(IReadOnlyList<CutPieceInstance> bar, CutPieceInstance piece)
  {
    if (bar.Count == 0)
      return 0;

    var score = 0;
    var profile = GetProfileKey(piece);

    score += bar.Count(existing => GetProfileKey(existing) == profile) * 3;

    if (CanPotentiallyShareCut(bar[^1], piece))
      score += 12;

    for (var i = 0; i < bar.Count - 1; i++)
    {
      if (CanPotentiallyShareCut(bar[i], piece))
        score += 4;
    }

    return score;
  }

  public static bool TryGetSingleMiterAngle(CutPieceInstance piece, out double angle)
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

  public static bool TryGetBothEqualMiterAngles(CutPieceInstance piece, out double angle)
  {
    angle = 0;
    if (piece.MiterEnd1Deg <= 0.1 || piece.MiterEnd2Deg <= 0.1)
      return false;

    if (Math.Abs(piece.MiterEnd1Deg - piece.MiterEnd2Deg) > 0.1)
      return false;

    angle = piece.MiterEnd1Deg;
    return true;
  }

  public static bool TryGetUnequalTrapezoidAngles(CutPieceInstance piece, out double angleA, out double angleB)
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
