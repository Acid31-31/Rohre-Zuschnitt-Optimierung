using System.IO;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class ManualTubeDrawingService
{
  public static string Create(
    string outputDirectory,
    string drawingNumber,
    CutPartEntry part,
    PipeProfileDefinition? profile,
    string? material,
    string? orderReference)
  {
    Directory.CreateDirectory(outputDirectory);
    var filePath = MakeUniquePath(Path.Combine(outputDirectory, Sanitize(drawingNumber) + ".pdf"));

    TechnicalTubeDrawingRenderer.Render(
      filePath,
      drawingNumber,
      profile,
      material,
      part.LengthMm,
      part.Quantity,
      part.MiterEnd1Deg,
      part.MiterEnd2Deg,
      orderReference);

    return filePath;
  }

  private static string Sanitize(string value)
  {
    var invalid = Path.GetInvalidFileNameChars();
    var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
    return string.IsNullOrWhiteSpace(cleaned) ? "RZO-ROHR" : cleaned;
  }

  private static string MakeUniquePath(string path)
  {
    if (!File.Exists(path))
      return path;

    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var name = Path.GetFileNameWithoutExtension(path);
    var extension = Path.GetExtension(path);
    for (var i = 2; i < 1000; i++)
    {
      var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
      if (!File.Exists(candidate))
        return candidate;
    }

    return path;
  }
}
