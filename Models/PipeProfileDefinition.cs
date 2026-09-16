namespace RohreZuschnittOptimierung.Models;

public enum PipeProfileKind
{
  Round,
  Square,
  Rectangular,
  CProfile,
  UProfile,
  TProfile,
  RoundBar
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
    PipeProfileKind.CProfile => "C-Profil",
    PipeProfileKind.UProfile => "U-Profil",
    PipeProfileKind.TProfile => "T-Profil",
    PipeProfileKind.RoundBar => "Vollstange",
    _ => "Profil"
  };

  public string FullLabel => $"{KindLabel} {Dimensions}";

  public string CutPlanHeading
  {
    get
    {
      var typeLabel = Kind switch
      {
        PipeProfileKind.CProfile => "C-Profil",
        PipeProfileKind.UProfile => "U-Profil",
        PipeProfileKind.TProfile => "T-Profil",
        PipeProfileKind.RoundBar => "Vollstange",
        _ => "Rohre"
      };
      return string.IsNullOrWhiteSpace(Dimensions) ? typeLabel : $"{typeLabel} {Dimensions}";
    }
  }
}
