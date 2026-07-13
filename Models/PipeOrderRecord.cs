namespace RohreZuschnittOptimierung.Models;

public sealed class PipeOrderRecord
{
  public string OrderReference { get; set; } = string.Empty;
  public PipeOrderStatus Status { get; set; } = PipeOrderStatus.Reserved;
  public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

  public string ProfileId { get; set; } = string.Empty;
  public string ProfileLabel { get; set; } = string.Empty;
  public string Material { get; set; } = PipeMaterialTypes.Steel;
  public double StockLengthMm { get; set; }
  public double KerfMm { get; set; }

  public List<CutPartEntry> Parts { get; set; } = [];
  public CutOptimizationResult Result { get; set; } = new();
  public List<PipeOrderLine> OrderLines { get; set; } = [];
  public int OrderedNewBarsCount { get; set; }
  public bool WarehouseBooked { get; set; }

  public string StatusLabel => PipeOrderStatusLabels.ToLabel(Status);

  public string SummaryLine =>
    $"{OrderReference} · {ProfileLabel} · {Material} · {StatusLabel}";
}
