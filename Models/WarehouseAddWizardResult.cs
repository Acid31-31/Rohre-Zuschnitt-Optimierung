using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung;

public sealed class WarehouseAddWizardResult
{
  public required PipeProfileDefinition Profile { get; init; }
  public required string Material { get; init; }
  public double LengthMm { get; init; }
  public int Quantity { get; init; }
  public bool IsNewMaterial { get; init; }
  public bool IsSelectionOnly { get; init; }
}
