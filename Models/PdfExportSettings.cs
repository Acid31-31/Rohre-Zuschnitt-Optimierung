namespace RohreZuschnittOptimierung.Models;

public sealed class PdfExportSettings
{
  public bool ShowCreatedDate { get; set; } = true;
  public bool ShowSummaryHeader { get; set; } = true;
  public bool ShowTotalSawSummary { get; set; } = true;
  public bool ShowPartsOverview { get; set; } = true;
  public bool ShowBarDiagram { get; set; } = true;
  public bool ShowBarUsageInfo { get; set; } = true;
  public bool ShowDiagramCutSequenceLine { get; set; }
  public bool ShowDetailedCutSequence { get; set; }
}
