using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace RohreZuschnittOptimierung.Services;

internal static class WorkshopDrawingPdfMergeService
{
  public static string? MergeDrawings(string outputPath, IEnumerable<string> sourcePaths)
  {
    var paths = sourcePaths
      .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (paths.Count == 0)
      return null;

    if (paths.Count == 1)
      return paths[0];

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);

    using var outputDocument = new PdfDocument();
    outputDocument.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
    outputDocument.Info.Author = AppInfo.ProductName;

    foreach (var path in paths)
    {
      using var inputDocument = PdfReader.Open(path, PdfDocumentOpenMode.Import);
      for (var i = 0; i < inputDocument.PageCount; i++)
        outputDocument.AddPage(inputDocument.Pages[i]);
    }

    if (outputDocument.PageCount == 0)
      return null;

    outputDocument.Save(outputPath);
    return outputPath;
  }

  public static string BuildCombinedDrawingPath(string outputDirectory, string orderReference) =>
    Path.Combine(
      outputDirectory,
      TechnicalTubeDrawingRenderer.SanitizeOrderReference(orderReference) + "_Zeichnungen.pdf");
}
