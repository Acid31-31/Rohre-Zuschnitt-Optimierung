using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class WorkshopTubeDrawingService
{
  public static string Create(
    string outputDirectory,
    string drawingNumber,
    string description,
    PipeProfileDefinition? profile,
    double lengthMm,
    int quantity,
    string? orderReference,
    string? sourceNote,
    string? assemblyPosition = null,
    string? assemblyFileName = null,
    double miterEnd1Deg = 0,
    double miterEnd2Deg = 0,
    string? subtitle = null)
  {
    Directory.CreateDirectory(outputDirectory);
    PdfFontBootstrap.EnsureInitialized();

    var fileName = Sanitize(drawingNumber) + ".pdf";
    var filePath = MakeUniquePath(Path.Combine(outputDirectory, fileName));

    using var document = new PdfDocument();
    document.Info.Title = drawingNumber;
    document.Info.Author = "Rohre Zuschnitt Optimierung";

    var page = document.AddPage();
    page.Size = PdfSharp.PageSize.A4;
    var gfx = XGraphics.FromPdfPage(page);

    var fontTitle = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
    var fontLabel = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
    var fontValue = new XFont("Segoe UI", 11, XFontStyleEx.Bold);
    var fontSmall = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
    var fontDim = new XFont("Segoe UI", 10, XFontStyleEx.Bold);

    var margin = 28.0;
    var width = page.Width - margin * 2;

    gfx.DrawRectangle(new XPen(XColors.Black, 1.2), margin, margin, width, page.Height - margin * 2);
    gfx.DrawString("WERKSTATTZEICHNUNG", fontTitle, XBrushes.Black, new XPoint(margin + 12, margin + 28));
    gfx.DrawString(
      subtitle ?? "Rohre Zuschnitt Optimierung – automatisch erzeugt (keine Hersteller-PDF)",
      fontSmall,
      XBrushes.DimGray,
      new XPoint(margin + 12, margin + 46));

    var viewTop = margin + 70;
    var viewHeight = 250;
    gfx.DrawRectangle(new XPen(XColors.Gray, 0.6), margin + 16, viewTop, width - 32, viewHeight);

    DrawTubeView(gfx, margin + 16, viewTop, width - 32, viewHeight, profile, lengthMm, miterEnd1Deg, miterEnd2Deg, fontDim, fontSmall);

    var boxTop = viewTop + viewHeight + 18;
    var col = (width - 24) / 2;
    var name = string.IsNullOrWhiteSpace(description) ? "TUBE" : TrimValue(description, 42);
    DrawField(gfx, margin + 12, boxTop, col, 48, "BAUTEIL BENENNUNG", name, fontLabel, fontValue);
    DrawField(gfx, margin + 20 + col, boxTop, col, 48, "ZEICHNUNGSNUMMER", drawingNumber, fontLabel, fontValue);

    DrawField(gfx, margin + 12, boxTop + 52, col, 48, "PROFIL / BENENNUNG 2", profile?.FullLabel ?? "—", fontLabel, fontValue);
    DrawField(gfx, margin + 20 + col, boxTop + 52, col, 48, "ROHRLÄNGE", lengthMm > 0.1 ? FormatMm(lengthMm) : "siehe Baugruppe", fontLabel, fontValue);

    DrawField(gfx, margin + 12, boxTop + 104, col, 48, "STÜCKZAHL", quantity.ToString("0"), fontLabel, fontValue);
    DrawField(gfx, margin + 20 + col, boxTop + 104, col, 48, "GEHRUNG", MiterNotation.Format(miterEnd1Deg, miterEnd2Deg), fontLabel, fontValue);

    DrawField(gfx, margin + 12, boxTop + 156, col, 48, "AUFTRAG", string.IsNullOrWhiteSpace(orderReference) ? "—" : TrimValue(orderReference, 28), fontLabel, fontValue);
    if (!string.IsNullOrWhiteSpace(assemblyFileName))
    {
      DrawField(gfx, margin + 20 + col, boxTop + 156, col, 48, "HAUPTZEICHNUNG", TrimValue(assemblyFileName, 28), fontLabel, fontValue);
    }
    else
    {
      DrawField(gfx, margin + 20 + col, boxTop + 156, col, 48, "POSITION (Baugruppe)", string.IsNullOrWhiteSpace(assemblyPosition) ? "—" : assemblyPosition, fontLabel, fontValue);
    }

    var noteTop = boxTop + 216;
    gfx.DrawString("HINWEIS", fontLabel, XBrushes.DimGray, new XPoint(margin + 12, noteTop));
    var note = "Diese Zeichnung wurde automatisch erzeugt, weil in der Bestellung kein PDF vorlag. "
               + "In der Werkstatt über Position in der Baugruppe, Länge, Profil und Stückzahl zuordnen."
               + (string.IsNullOrWhiteSpace(sourceNote) ? string.Empty : " " + sourceNote);
    gfx.DrawString(note, fontSmall, XBrushes.Black, new XRect(margin + 12, noteTop + 8, width - 24, 70), XStringFormats.TopLeft);

    gfx.DrawString("FORMAT A4  ·  MASSSTAB schematisch  ·  REV. 00", fontSmall, XBrushes.DimGray, new XPoint(margin + 12, page.Height - margin - 16));

    document.Save(filePath);
    return filePath;
  }

  public static string EnsureDrawingForPart(
    CutPartEntry part,
    string outputDirectory,
    PipeProfileDefinition? profile,
    string? material,
    string? orderReference,
    ref int sequence)
  {
    if (!string.IsNullOrWhiteSpace(part.PdfPath) && File.Exists(part.PdfPath))
      return part.PdfPath;

    var drawingNumber = TechnicalTubeDrawingRenderer.BuildManualDrawingNumber(orderReference, sequence);
    sequence++;

    var path = ManualTubeDrawingService.Create(
      outputDirectory,
      drawingNumber,
      part,
      profile,
      material,
      orderReference);

    part.PdfPath = path;
    part.DrawingName = drawingNumber;

    return path;
  }

  private static void DrawTubeView(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    PipeProfileDefinition? profile,
    double lengthMm,
    double miterEnd1Deg,
    double miterEnd2Deg,
    XFont fontDim,
    XFont fontSmall)
  {
    var tubeHeight = 36.0;
    var tubeWidth = Math.Min(w - 80, 420);
    var tubeX = x + (w - tubeWidth) / 2;
    var tubeY = y + h / 2 - tubeHeight / 2 - 10;

    var leftInset = MiterInset(tubeHeight, miterEnd1Deg);
    var rightInset = MiterInset(tubeHeight, miterEnd2Deg);
    var outline = new[]
    {
      new XPoint(tubeX + leftInset, tubeY),
      new XPoint(tubeX + tubeWidth - rightInset, tubeY),
      new XPoint(tubeX + tubeWidth, tubeY + tubeHeight),
      new XPoint(tubeX, tubeY + tubeHeight)
    };
    gfx.DrawPolygon(new XPen(XColors.Black, 1.6), XBrushes.White, outline, XFillMode.Alternate);
    gfx.DrawLine(new XPen(XColors.Black, 0.8), tubeX + leftInset, tubeY + 8, tubeX + tubeWidth - rightInset, tubeY + 8);
    gfx.DrawLine(new XPen(XColors.Black, 0.8), tubeX, tubeY + tubeHeight - 8, tubeX + tubeWidth, tubeY + tubeHeight - 8);

    if (miterEnd1Deg > 0.1)
      gfx.DrawString($"A {miterEnd1Deg:0}°", fontSmall, XBrushes.Black, new XPoint(tubeX + 4, tubeY + tubeHeight + 2));
    if (miterEnd2Deg > 0.1)
      gfx.DrawString($"B {miterEnd2Deg:0}°", fontSmall, XBrushes.Black, new XPoint(tubeX + tubeWidth - 34, tubeY + tubeHeight + 2));

    var dimY = tubeY + tubeHeight + 28;
    gfx.DrawLine(new XPen(XColors.Black, 0.7), tubeX, dimY, tubeX + tubeWidth, dimY);
    gfx.DrawLine(new XPen(XColors.Black, 0.7), tubeX, dimY - 6, tubeX, dimY + 6);
    gfx.DrawLine(new XPen(XColors.Black, 0.7), tubeX + tubeWidth, dimY - 6, tubeX + tubeWidth, dimY + 6);
    gfx.DrawString(lengthMm > 0.1 ? FormatMm(lengthMm) : "L siehe Baugruppe", fontDim, XBrushes.Black, new XRect(tubeX, dimY - 22, tubeWidth, 18), XStringFormats.Center);

    var section = profile?.Dimensions ?? "";
    gfx.DrawString(
      string.IsNullOrWhiteSpace(section) ? "TUBE" : "TUBE  " + section,
      fontSmall,
      XBrushes.Black,
      new XRect(tubeX, tubeY - 22, tubeWidth, 16),
      XStringFormats.Center);
  }

  private static double MiterInset(double tubeHeight, double miterDeg)
  {
    if (miterDeg <= 0.1)
      return 0;

    var radians = miterDeg * Math.PI / 180.0;
    return Math.Min(tubeHeight * 0.45, tubeHeight * Math.Tan(radians) * 0.35);
  }

  private static void DrawField(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    string label,
    string value,
    XFont fontLabel,
    XFont fontValue)
  {
    gfx.DrawRectangle(new XPen(XColors.Black, 0.8), x, y, w, h);
    gfx.DrawString(label, fontLabel, XBrushes.DimGray, new XPoint(x + 8, y + 14));
    gfx.DrawString(value, fontValue, XBrushes.Black, new XRect(x + 8, y + 20, w - 16, h - 24), XStringFormats.TopLeft);
  }

  private static string FormatMm(double mm) =>
    mm.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " mm";

  private static string TrimValue(string value, int maxChars) =>
    value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "…";

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
