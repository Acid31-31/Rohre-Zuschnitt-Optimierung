namespace RohreZuschnittOptimierung.Models;

public sealed class StockRemnantEntry
{
  public double LengthMm { get; set; }
  public int Quantity { get; set; } = 1;
  public bool IsFullBar { get; set; }
}
