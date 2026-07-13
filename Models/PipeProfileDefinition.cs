namespace RohreZuschnittOptimierung.Models;

public enum PipeProfileKind
{
  Round,
  Square,
  Rectangular
}

public sealed class PipeProfileDefinition
{
  public required string Id { get; init; }
  public PipeProfileKind Kind { get; init; }
  public required string DisplayName { get; init; }
  public required string Dimensions { get; init; }
  public string Material { get; init; } = PipeMaterialTypes.Steel;

  public string KindLabel => Kind switch
  {
    PipeProfileKind.Round => "Rundrohr",
    PipeProfileKind.Square => "Vierkantrohr",
    PipeProfileKind.Rectangular => "Rechteckrohr",
    _ => "Rohr"
  };

  public string FullLabel => $"{KindLabel} {Dimensions}";
}
