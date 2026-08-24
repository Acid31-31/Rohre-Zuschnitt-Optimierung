namespace RohreZuschnittOptimierung.Models;

public enum DrawingPartKind
{
  Unknown,
  Pipe,
  SheetMetal
}

public enum AnalysisValueSource
{
  None,
  Rules,
  Step,
  LocalAi
}

public sealed class PdfDrawingAnalysisResult
{
  public double? LengthMm { get; init; }
  public double? MiterEnd1Deg { get; init; }
  public double? MiterEnd2Deg { get; init; }
  public PipeProfileDefinition? Profile { get; init; }
  public string? Material { get; init; }
  public string? PartName { get; init; }
  public DrawingPartKind Kind { get; init; } = DrawingPartKind.Unknown;
  public string Summary { get; init; } = string.Empty;
  public AnalysisValueSource LengthSource { get; init; } = AnalysisValueSource.None;
  public AnalysisValueSource MiterSource { get; init; } = AnalysisValueSource.None;
  public bool HasLength => LengthMm is > 0;
  public bool HasProfile => Profile is not null;
  public bool IsPipe => Kind == DrawingPartKind.Pipe;
}

public sealed class PdfFileOption
{
  public required string FileName { get; init; }
  public required string FullPath { get; init; }

  public override string ToString() => FileName;
}
