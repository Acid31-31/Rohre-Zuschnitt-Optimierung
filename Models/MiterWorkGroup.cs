namespace RohreZuschnittOptimierung.Models;

public sealed class MiterWorkGroup
{
  public double AngleDeg { get; init; }
  public int CutCount { get; init; }
  public IReadOnlyList<string> Items { get; init; } = [];
}
