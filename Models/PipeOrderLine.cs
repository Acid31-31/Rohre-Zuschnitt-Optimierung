namespace RohreZuschnittOptimierung.Models;

public sealed class PipeOrderLine
{
  public string ProfileId { get; init; } = string.Empty;
  public string ProfileLabel { get; init; } = string.Empty;
  public string Material { get; init; } = PipeMaterialTypes.Steel;
  public double LengthMm { get; init; }
  public int Quantity { get; init; }

  public string Summary => $"{Quantity}× {ProfileLabel} · {LengthMm:0} mm · {Material}";
}
