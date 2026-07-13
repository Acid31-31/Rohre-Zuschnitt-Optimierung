namespace RohreZuschnittOptimierung.Models;

public sealed class PdfDrawingAnalysisResult
{
  public double? LengthMm { get; init; }
  public double? MiterEnd1Deg { get; init; }
  public double? MiterEnd2Deg { get; init; }
  public string Summary { get; init; } = string.Empty;
  public bool HasLength => LengthMm is > 0;
}

public sealed class PdfFileOption
{
  public required string FileName { get; init; }
  public required string FullPath { get; init; }

  public override string ToString() => FileName;
}
