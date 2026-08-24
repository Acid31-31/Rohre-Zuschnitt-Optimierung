using System.IO;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PdfExportSettingsStore
{
  private const string FileName = "pdf-export-settings.xml";

  public static string FilePath =>
    Path.Combine(AppInfo.UserDataDirectory, FileName);

  public static PdfExportSettings Load()
  {
    if (!File.Exists(FilePath))
      return new PdfExportSettings();

    try
    {
      var root = XDocument.Load(FilePath).Root;
      if (root is null)
        return new PdfExportSettings();

      return new PdfExportSettings
      {
        ShowCreatedDate = ReadBool(root, nameof(PdfExportSettings.ShowCreatedDate), true),
        ShowSummaryHeader = ReadBool(root, nameof(PdfExportSettings.ShowSummaryHeader), true),
        ShowTotalSawSummary = ReadBool(root, nameof(PdfExportSettings.ShowTotalSawSummary), true),
        ShowPartsOverview = ReadBool(root, nameof(PdfExportSettings.ShowPartsOverview), true),
        ShowBarDiagram = ReadBool(root, nameof(PdfExportSettings.ShowBarDiagram), true),
        ShowBarUsageInfo = ReadBool(root, nameof(PdfExportSettings.ShowBarUsageInfo), true),
        ShowDiagramCutSequenceLine = ReadBool(root, nameof(PdfExportSettings.ShowDiagramCutSequenceLine), false),
        ShowDetailedCutSequence = ReadBool(root, nameof(PdfExportSettings.ShowDetailedCutSequence), false)
      };
    }
    catch
    {
      return new PdfExportSettings();
    }
  }

  public static void Save(PdfExportSettings settings)
  {
    var directory = Path.GetDirectoryName(FilePath)!;
    Directory.CreateDirectory(directory);

    var root = new XElement(
      "PdfExportSettings",
      BoolElement(nameof(PdfExportSettings.ShowCreatedDate), settings.ShowCreatedDate),
      BoolElement(nameof(PdfExportSettings.ShowSummaryHeader), settings.ShowSummaryHeader),
      BoolElement(nameof(PdfExportSettings.ShowTotalSawSummary), settings.ShowTotalSawSummary),
      BoolElement(nameof(PdfExportSettings.ShowPartsOverview), settings.ShowPartsOverview),
      BoolElement(nameof(PdfExportSettings.ShowBarDiagram), settings.ShowBarDiagram),
      BoolElement(nameof(PdfExportSettings.ShowBarUsageInfo), settings.ShowBarUsageInfo),
      BoolElement(nameof(PdfExportSettings.ShowDiagramCutSequenceLine), settings.ShowDiagramCutSequenceLine),
      BoolElement(nameof(PdfExportSettings.ShowDetailedCutSequence), settings.ShowDetailedCutSequence));

    root.Save(FilePath);
  }

  private static bool ReadBool(XElement root, string name, bool fallback) =>
    bool.TryParse((string?)root.Element(name), out var value) ? value : fallback;

  private static XElement BoolElement(string name, bool value) =>
    new(name, value.ToString().ToLowerInvariant());
}
