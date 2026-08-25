using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class OrderListPdfExportService
{
  private const double MarginPt = 42;
  private const double LineHeight = 14;

  public static void Export(
    string filePath,
    string orderReference,
    PipeProfileDefinition profile,
    string material,
    WarehouseReservationResult reservation,
    IReadOnlyList<PipeOrderLine> orderLines,
    CutOptimizationResult result,
    int warehouseFreeCount = 0)
  {
    PdfFontBootstrap.EnsureInitialized();

    using var document = new PdfDocument();
    document.Info.Title = "Bestellliste Rohrmaterial";
    document.Info.Author = "Rohre Zuschnitt Optimierung";

    var page = document.AddPage();
    page.Size = PdfSharp.PageSize.A4;
    var gfx = XGraphics.FromPdfPage(page);

    var fontTitle = new XFont("Segoe UI", 18, XFontStyleEx.Bold);
    var fontHeading = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
    var fontBody = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
    var fontSmall = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
    var fontBold = new XFont("Segoe UI", 10, XFontStyleEx.Bold);

    var y = MarginPt;
    var contentWidth = page.Width - MarginPt * 2;

    y = DrawLine(gfx, "Bestellliste Rohrmaterial", fontTitle, MarginPt, y, contentWidth);
    y += 4;
    y = DrawLine(gfx, $"Auftrag: {orderReference}", fontBold, MarginPt, y, contentWidth);
    y = DrawLine(gfx, $"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", fontSmall, MarginPt, y, contentWidth);
    y += 10;

    y = DrawLine(gfx, "Profil", fontHeading, MarginPt, y, contentWidth);
    y = DrawLine(gfx, profile.FullLabel, fontBody, MarginPt, y, contentWidth);
    y = DrawLine(gfx, $"Materialart: {material}", fontBody, MarginPt, y, contentWidth);
    y = DrawLine(
      gfx,
      warehouseFreeCount > 0
        ? $"Lager frei für dieses Profil: {warehouseFreeCount} Stange(n) — fehlende Stangen werden bestellt."
        : "Lager frei für dieses Profil: 0 Stange(n). Deshalb Bestellliste — zuerst unter Lager die Stückzahl eintragen.",
      fontBody,
      MarginPt,
      y,
      contentWidth);
    y += 8;

    if (orderLines.Count > 0)
    {
      y = DrawLine(gfx, "Fehlmenge — bitte nachbestellen", fontHeading, MarginPt, y, contentWidth);
      y += 4;

      foreach (var line in orderLines)
      {
        y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, contentWidth);
        y = DrawLine(
          gfx,
          $"· {line.Quantity}× {line.ProfileLabel} · {FormatMm(line.LengthMm)} · {line.Material}",
          fontBold,
          MarginPt,
          y,
          contentWidth);
      }

      var missingTotalMm = orderLines.Sum(line => line.Quantity * line.LengthMm);
      y += 4;
      y = DrawLine(
        gfx,
        $"Gesamt fehlend: {orderLines.Sum(line => line.Quantity)} Stange(n) · {FormatMeters(missingTotalMm)}",
        fontBody,
        MarginPt,
        y,
        contentWidth);
      y += 10;
    }

    if (reservation.ReservedBarsCount > 0)
    {
      y = DrawLine(gfx, "Aus Lager reserviert (für diesen Auftrag)", fontHeading, MarginPt, y, contentWidth);
      y += 4;

      foreach (var line in reservation.ReservedLines)
      {
        y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, contentWidth);
        var typeLabel = line.IsFullBar ? "Originalstange" : "Rohrest";
        y = DrawLine(
          gfx,
          $"· {line.Quantity}× {typeLabel} · {FormatMm(line.LengthMm)} · {material}",
          fontBody,
          MarginPt,
          y,
          contentWidth);
      }

      y += 4;
      y = DrawLine(
        gfx,
        $"Reserviert gesamt: {reservation.ReservedBarsCount} Stange(n)",
        fontBody,
        MarginPt,
        y,
        contentWidth);
      y += 10;
    }

    y = DrawLine(gfx, "Schnittplan-Kurzinfo", fontHeading, MarginPt, y, contentWidth);
    y = DrawLine(
      gfx,
      $"{result.TotalBars} Stange(n) geplant · {result.Bars.Sum(bar => bar.Pieces.Count)} Teil(e) · Schnitt {FormatMm(result.KerfMm)}",
      fontBody,
      MarginPt,
      y,
      contentWidth);

    if (orderLines.Count > 0)
    {
      y += 12;
      y = DrawLine(
        gfx,
        "Hinweis: Nach Lieferung unter „Lager → Aufträge“ den Auftrag öffnen, "
        + "„Material eingetroffen“ buchen und nach dem Schneiden „Schnitt verbuchen“.",
        fontSmall,
        MarginPt,
        y,
        contentWidth);
    }

    document.Save(filePath);
  }

  private static double EnsureSpace(
    PdfDocument document,
    ref PdfPage page,
    ref XGraphics gfx,
    ref double y,
    double requiredHeight,
    double contentWidth)
  {
    if (y + requiredHeight <= page.Height - MarginPt)
      return y;

    gfx.Dispose();
    page = document.AddPage();
    page.Size = PdfSharp.PageSize.A4;
    gfx = XGraphics.FromPdfPage(page);
    y = MarginPt;
    return y;
  }

  private static double DrawLine(
    XGraphics gfx,
    string text,
    XFont font,
    double x,
    double y,
    double maxWidth)
  {
    foreach (var line in WrapText(text, font, gfx, maxWidth))
    {
      gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, y));
      y += LineHeight;
    }

    return y;
  }

  private static List<string> WrapText(string text, XFont font, XGraphics gfx, double maxWidth)
  {
    if (string.IsNullOrWhiteSpace(text))
      return [string.Empty];

    var words = text.Split(' ');
    var lines = new List<string>();
    var current = string.Empty;

    foreach (var word in words)
    {
      var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
      if (gfx.MeasureString(candidate, font).Width <= maxWidth)
      {
        current = candidate;
        continue;
      }

      if (!string.IsNullOrEmpty(current))
        lines.Add(current);

      current = word;
    }

    if (!string.IsNullOrEmpty(current))
      lines.Add(current);

    return lines.Count == 0 ? [text] : lines;
  }

  private static string FormatMm(double valueMm) =>
    Math.Abs(valueMm - Math.Round(valueMm)) < 0.01
      ? $"{valueMm.ToString("0", CultureInfo.InvariantCulture)} mm"
      : $"{valueMm.ToString("0.##", CultureInfo.InvariantCulture)} mm";

  private static string FormatMeters(double valueMm) =>
    $"{(valueMm / 1000).ToString("0.##", CultureInfo.InvariantCulture)} m";
}
