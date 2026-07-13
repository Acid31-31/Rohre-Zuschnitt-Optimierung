namespace RohreZuschnittOptimierung.Models;

public sealed class WarehouseReservationResult
{
  public string OrderReference { get; init; } = string.Empty;
  public List<ReservedStockLine> ReservedLines { get; init; } = [];
  public List<ReservedStockLine> ReturnedRemnantLines { get; init; } = [];
  public int ReservedBarsCount => ReservedLines.Sum(line => line.Quantity);
  public int ReturnedRemnantCount => ReturnedRemnantLines.Sum(line => line.Quantity);
}

public sealed class ReservedStockLine
{
  public double LengthMm { get; init; }
  public int Quantity { get; init; }
  public bool IsFullBar { get; init; }

  public string Summary =>
    $"{Quantity}× {(IsFullBar ? "Originalstange" : "Rohrest")} {LengthMm:0} mm";
}
