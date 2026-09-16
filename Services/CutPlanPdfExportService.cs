using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class CutPlanPdfExportService
{
  private const double MarginPt = 42;
  private const double LineHeight = 14;

  public static void Export(
    string filePath,
    CutOptimizationResult result,
    IReadOnlyList<CutPartEntry> parts,
    PdfExportSettings? settings = null,
    string? orderReference = null,
    TimeSpan? processingDuration = null)
  {
    settings ??= PdfExportSettingsStore.Load();
    PdfFontBootstrap.EnsureInitialized();

    var title = BuildDocumentTitle(result, parts);

    using var document = new PdfDocument();
    document.Info.Title = title;
    document.Info.Author = "Rohre Zuschnitt Optimierung";

    var page = AddLandscapePage(document);
    var gfx = XGraphics.FromPdfPage(page);

    var fontTitle = new XFont("Segoe UI", 18, XFontStyleEx.Bold);
    var fontHeading = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
    var fontBody = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
    var fontSmall = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
    var fontBold = new XFont("Segoe UI", 10, XFontStyleEx.Bold);

    var y = MarginPt;
    var contentWidth = page.Width - MarginPt * 2;

    y = DrawLine(gfx, title, fontTitle, MarginPt, y, contentWidth);
    y += 4;

    if (!string.IsNullOrWhiteSpace(orderReference))
    {
      y = DrawLine(gfx, $"Auftrag: {orderReference}", fontBold, MarginPt, y, contentWidth);
      y += 4;
    }

    if (settings.ShowCreatedDate)
      y = DrawLine(gfx, $"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", fontSmall, MarginPt, y, contentWidth);

    if (processingDuration is { } duration && duration > TimeSpan.Zero)
      y = DrawLine(
        gfx,
        $"Gesamtbearbeitungszeit: {duration.ToString(@"hh\:mm\:ss")}",
        fontBold,
        MarginPt,
        y,
        contentWidth);

    if (settings.ShowCreatedDate || processingDuration is { } d && d > TimeSpan.Zero)
      y += 10;

    if (settings.ShowSummaryHeader)
    {
      y = DrawLine(
        gfx,
        $"Originalstange: {FormatMm(result.StockLengthMm)} · Schnittbreite: {FormatMm(result.KerfMm)}",
        fontBody,
        MarginPt,
        y,
        contentWidth);

      if (result.RemnantBarsUsed > 0)
      {
        y = DrawLine(
          gfx,
          $"Verwendet: {result.RemnantBarsUsed} Rohrest(e), {result.NewOriginalBarsUsed} neue Originalstange(n)",
          fontBody,
          MarginPt,
          y,
          contentWidth);
      }

      var totalPieces = result.Bars.Sum(bar => bar.Pieces.Count);
      y = DrawLine(
        gfx,
        $"{result.TotalBars} Stange(n) · {totalPieces} Teil(e) · Verschnitt gesamt: {FormatMm(result.TotalWasteMm)}",
        fontBody,
        MarginPt,
        y,
        contentWidth);
    }

    if (settings.ShowTotalSawSummary && !string.IsNullOrWhiteSpace(result.SawPlanSummary))
      y = DrawLine(gfx, result.SawPlanSummary, fontBody, MarginPt, y, contentWidth);

    if (settings.ShowSummaryHeader || settings.ShowTotalSawSummary)
      y += 12;

    if (settings.ShowPartsOverview)
    {
      y = DrawLine(gfx, "Teileübersicht", fontHeading, MarginPt, y, contentWidth);
      y += 4;

      foreach (var part in parts)
      {
        var name = string.IsNullOrWhiteSpace(part.DrawingName) ? "—" : part.DrawingName;
        y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
        y = DrawLine(
          gfx,
          $"· {name} · {FormatMm(part.LengthMm)} · Gehrung {MiterNotation.Format(part.MiterEnd1Deg, part.MiterEnd2Deg)} · {part.Quantity}×",
          fontBody,
          MarginPt,
          y,
          contentWidth);
      }

      y += 10;
    }

    foreach (var bar in result.Bars)
    {
      var oriented = bar.OrientedPieces.Count > 0
        ? bar.OrientedPieces
        : MiterPairingService.OrientPiecesOnBar(bar.Pieces);
      var steps = bar.StockCutSteps.Count > 0
        ? bar.StockCutSteps
        : MiterPairingService.BuildStockCutSteps(oriented);
      var tableRows = BuildSawWorkRows(oriented, steps);
      var requiredHeight = 80
                           + (settings.ShowBarDiagram ? 120 : 0)
                           + 28 + tableRows.Count * (LineHeight + 1);
      y = EnsureSpace(document, ref page, ref gfx, ref y, requiredHeight, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
      y = DrawLine(gfx, $"{bar.StockLabel} {bar.BarNumber} · {FormatMm(bar.StockLengthMm)}", fontHeading, MarginPt, y, contentWidth);

      if (settings.ShowBarUsageInfo)
      {
        y = DrawLine(
          gfx,
          $"Genutzt {FormatMm(bar.UsedMm)} · Rest {FormatMm(bar.WasteMm)} · Säge {bar.SawAdjustments}× verstellen",
          fontSmall,
          MarginPt,
          y,
          contentWidth);
      }

      y = DrawLine(
        gfx,
        "Farbe = Gehrung. Rechteck = Rohr. Schräge Linie = ein Schnitt, kein Dreieck.",
        fontBold,
        MarginPt,
        y,
        contentWidth);

      y += 6;

      if (settings.ShowBarDiagram)
        y = DrawBarDiagram(gfx, bar, bar.StockLengthMm, MarginPt, y, contentWidth);
      else
        y += 4;

      y = DrawSawWorkTable(document, ref page, ref gfx, ref y, tableRows, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);

      y += 14;
    }

    gfx.Dispose();
    document.Save(filePath);
  }

  private static double DrawBarDiagram(
    XGraphics gfx,
    CutBarPlan bar,
    double stockLengthMm,
    double x,
    double y,
    double width)
  {
    const double labelBand = 14;
    const double barHeight = 44;
    var visibleLengthMm = stockLengthMm > 0 ? stockLengthMm : bar.StockLengthMm;
    var scale = width / visibleLengthMm;
    var barTop = y + labelBand;
    var barBottom = barTop + barHeight;

    var wasteBrush = new XSolidBrush(XColor.FromArgb(230, 230, 230));
    var textBrush = XBrushes.Black;
    var fontLabel = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
    var fontSmall = new XFont("Segoe UI", 7, XFontStyleEx.Bold);
    var cutPen = new XPen(XColors.Black, 2);

    var stockWidth = visibleLengthMm * scale;
    var usedWidth = Math.Min(bar.UsedMm * scale, stockWidth);
    if (stockWidth - usedWidth > 1)
      gfx.DrawRectangle(wasteBrush, x + usedWidth, barTop, stockWidth - usedWidth, barHeight);

    if (bar.WasteMm > 0 && stockWidth - usedWidth > 28)
    {
      DrawCenteredMultilineText(
        gfx,
        ["Verschnitt", $"{bar.WasteMm:0} mm"],
        fontSmall,
        textBrush,
        new XRect(x + usedWidth, barBottom - 20, stockWidth - usedWidth, 18));
    }

    var oriented = bar.OrientedPieces.Count > 0
      ? bar.OrientedPieces.ToList()
      : MiterPairingService.OrientPiecesOnBar(bar.Pieces);
    var cutAngles = MiterPairingService.BuildCutAnglesOnBar(oriented).ToList();
    var gehrungIndex = MiterPairingService.GehrungColorIndex(oriented);

    var lefts = new double[oriented.Count];
    var rights = new double[oriented.Count];
    var cursorX = x;
    for (var i = 0; i < oriented.Count; i++)
    {
      var pieceWidth = Math.Max(oriented[i].LengthMm * scale, 1);
      lefts[i] = cursorX;
      rights[i] = cursorX + pieceWidth;
      var (fill, _) = BrushForGehrung(MiterPairingService.PrimaryMiterDeg(oriented[i]), gehrungIndex);
      gfx.DrawRectangle(fill, cursorX, barTop, pieceWidth, barHeight);

      if (pieceWidth >= 32)
      {
        DrawCenteredMultilineText(
          gfx,
          [$"{i + 1}", $"{oriented[i].LengthMm:0} mm"],
          fontLabel,
          textBrush,
          new XRect(cursorX, barTop + 8, pieceWidth, barHeight - 12));
      }
      else if (pieceWidth >= 14)
      {
        DrawCenteredMultilineText(
          gfx,
          [$"{i + 1}"],
          fontSmall,
          textBrush,
          new XRect(cursorX, barTop + 14, pieceWidth, 16));
      }

      cursorX += pieceWidth;
    }

    if (oriented.Count > 0)
    {
      DrawSawCut(gfx, cutPen, lefts[0], barTop, barBottom, cutAngles[0]);
      for (var i = 0; i < oriented.Count - 1; i++)
        DrawSawCut(gfx, cutPen, rights[i], barTop, barBottom, cutAngles[i + 1]);
      DrawSawCut(gfx, cutPen, rights[^1], barTop, barBottom, cutAngles[^1]);
    }

    foreach (var run in GehrungRuns(oriented, lefts, rights))
    {
      gfx.DrawString(
        $"{run.Angle:0}°",
        fontSmall,
        textBrush,
        new XRect(run.Left, y, run.Right - run.Left, labelBand),
        XStringFormats.Center);
    }

    gfx.DrawString(
      "0 mm",
      fontSmall,
      textBrush,
      new XRect(x, barBottom + 4, 40, 10),
      XStringFormats.TopLeft);
    gfx.DrawString(
      $"{visibleLengthMm:0} mm",
      fontSmall,
      textBrush,
      new XRect(x + stockWidth - 56, barBottom + 4, 56, 10),
      XStringFormats.TopRight);

    if (gehrungIndex.Count > 0)
    {
      var legend = "Farbe = Gehrung, nicht Länge: "
                   + string.Join(" · ", gehrungIndex.OrderBy(e => e.Value).Select(e => $"{GehrungColorName(e.Value)} = {e.Key:0}°"));
      gfx.DrawString(
        legend,
        fontSmall,
        textBrush,
        new XRect(x, barBottom + 16, width, 12),
        XStringFormats.TopLeft);
      return y + labelBand + barHeight + 30;
    }

    return y + labelBand + barHeight + 16;
  }

  private static (XSolidBrush Fill, XPen Pen) BrushForGehrung(
    double miterDeg,
    IReadOnlyDictionary<double, int> gehrungIndex)
  {
    if (miterDeg <= 0.1 || Math.Abs(miterDeg - 90) < 0.1)
      return (new XSolidBrush(XColor.FromArgb(210, 210, 210)), new XPen(XColors.Gray, 1));

    var index = gehrungIndex.TryGetValue(miterDeg, out var mapped) ? mapped : 0;
    var (fill, stroke) = index switch
    {
      0 => (XColor.FromArgb(77, 163, 255), XColor.FromArgb(37, 99, 235)),
      1 => (XColor.FromArgb(34, 197, 94), XColor.FromArgb(22, 163, 74)),
      2 => (XColor.FromArgb(245, 158, 11), XColor.FromArgb(180, 100, 10)),
      _ => (XColor.FromArgb(168, 85, 247), XColor.FromArgb(110, 50, 180))
    };
    return (new XSolidBrush(fill), new XPen(stroke, 1.2));
  }

  private static string GehrungColorName(int index) => index switch
  {
    0 => "Blau",
    1 => "Grün",
    2 => "Orange",
    _ => "Violett"
  };

  private static bool IsMiterAngle(double deg) => deg > 0.5 && Math.Abs(deg - 90) > 0.5;

  private static void DrawSawCut(XGraphics gfx, XPen pen, double x, double yTop, double yBottom, double angleDeg)
  {
    if (IsMiterAngle(angleDeg))
    {
      DrawMiterSlash(gfx, pen, x, yTop, yBottom, angleDeg);
      return;
    }

    gfx.DrawLine(pen, x, yTop, x, yBottom);
  }

  private static void DrawMiterSlash(XGraphics gfx, XPen pen, double x, double yTop, double yBottom, double angleDeg)
  {
    var height = yBottom - yTop;
    var offset = Math.Min(14, height * Math.Tan(Math.Min(angleDeg, 60) * Math.PI / 180d) / 2);
    gfx.DrawLine(pen, x, yBottom, x - offset, yTop);
  }

  private readonly record struct GehrungRun(double Angle, double Left, double Right);

  private static List<GehrungRun> GehrungRuns(
    IReadOnlyList<OrientedCutPiece> pieces,
    IReadOnlyList<double> lefts,
    IReadOnlyList<double> rights)
  {
    var runs = new List<GehrungRun>();
    if (pieces.Count == 0)
      return runs;

    var start = 0;
    var angle = MiterPairingService.PrimaryMiterDeg(pieces[0]);
    for (var i = 1; i <= pieces.Count; i++)
    {
      var next = i < pieces.Count ? MiterPairingService.PrimaryMiterDeg(pieces[i]) : double.NaN;
      if (i < pieces.Count && Math.Abs(next - angle) < 0.1)
        continue;

      if (IsMiterAngle(angle) && rights[i - 1] - lefts[start] >= 28)
        runs.Add(new GehrungRun(angle, lefts[start], rights[i - 1]));

      if (i < pieces.Count)
      {
        start = i;
        angle = next;
      }
    }

    return runs;
  }

  private static double DrawSawWorkTable(
    PdfDocument document,
    ref PdfPage page,
    ref XGraphics gfx,
    ref double y,
    IReadOnlyList<SawWorkRow> rows,
    XFont fontBody,
    XFont fontHeading,
    XFont fontSmall,
    XFont fontTitle,
    XFont fontBold,
    double contentWidth)
  {
    y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight * 3, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
    y = DrawLine(gfx, "Säge – ein Schnitt nach dem anderen", fontHeading, MarginPt, y, contentWidth);
    y = DrawLine(
      gfx,
      "Nr.        Anschlag         Winkel      Hinweis",
      fontBold,
      MarginPt,
      y,
      contentWidth);

    foreach (var row in rows)
    {
      y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
      var stop = row.StopMm <= 0.1 ? "—" : FormatMm(row.StopMm);
      y = DrawLine(
        gfx,
        $"{row.Number,-6} {stop,-16} {row.AngleDeg:0}°         {row.Hint}",
        fontBody,
        MarginPt,
        y,
        contentWidth);
    }

    return y;
  }

  private static List<SawWorkRow> BuildSawWorkRows(
    IReadOnlyList<OrientedCutPiece> pieces,
    IReadOnlyList<StockCutStep> steps)
  {
    var rows = new List<SawWorkRow>();
    if (steps.Count == 0)
      return rows;

    for (var i = 0; i < steps.Count; i++)
    {
      var stopMm = i == 0 ? 0 : pieces[Math.Min(i - 1, pieces.Count - 1)].LengthMm;
      var hint = i == 0
        ? "Stangenanfang"
        : steps[i].IsSharedMiter
          ? "Gemeinsam – nur einmal sägen"
          : Math.Abs(steps[i].SawAngleDeg - 90) < 0.1
            ? "Lotrecht trennen"
            : steps[i].Description;

      rows.Add(new SawWorkRow(steps[i].StepNumber, stopMm, steps[i].SawAngleDeg, hint));
    }

    return rows;
  }

  private readonly record struct SawWorkRow(int Number, double StopMm, double AngleDeg, string Hint);

  private static double EnsureSpace(
    PdfDocument document,
    ref PdfPage page,
    ref XGraphics gfx,
    ref double y,
    double requiredHeight,
    XFont fontBody,
    XFont fontHeading,
    XFont fontSmall,
    XFont fontTitle,
    XFont fontBold,
    double contentWidth)
  {
    if (y + requiredHeight <= page.Height - MarginPt)
      return y;

    gfx.Dispose();
    page = AddLandscapePage(document);
    gfx = XGraphics.FromPdfPage(page);
    y = MarginPt;
    return y;
  }

  private static PdfPage AddLandscapePage(PdfDocument document)
  {
    var page = document.AddPage();
    page.Size = PdfSharp.PageSize.A4;
    page.Orientation = PdfSharp.PageOrientation.Landscape;
    return page;
  }

  private static string BuildDocumentTitle(CutOptimizationResult result, IReadOnlyList<CutPartEntry> parts)
  {
    var profile = ResolveProfile(result, parts);
    if (profile is not null)
      return $"Zuschnittplan {profile.CutPlanHeading}";

    if (!string.IsNullOrWhiteSpace(result.ProfileLabel))
      return $"Zuschnittplan {result.ProfileLabel}";

    return "Zuschnittplan Rohre";
  }

  private static PipeProfileDefinition? ResolveProfile(
    CutOptimizationResult result,
    IReadOnlyList<CutPartEntry> parts)
  {
    if (!string.IsNullOrWhiteSpace(result.ProfileId))
    {
      var fromResult = PipeStockCatalog.TryGet(result.ProfileId);
      if (fromResult is not null)
        return fromResult;
    }

    var partId = parts.FirstOrDefault(part => !string.IsNullOrWhiteSpace(part.ProfileId))?.ProfileId;
    return string.IsNullOrWhiteSpace(partId) ? null : PipeStockCatalog.TryGet(partId);
  }

  private static double DrawLine(
    XGraphics gfx,
    string text,
    XFont font,
    double x,
    double y,
    double maxWidth)
  {
    var lines = WrapText(text, font, gfx, maxWidth);
    foreach (var line in lines)
    {
      gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, y));
      y += LineHeight;
    }

    return y;
  }

  private static void DrawCenteredMultilineText(
    XGraphics gfx,
    IReadOnlyList<string> lines,
    XFont font,
    XBrush brush,
    XRect rect)
  {
    if (lines.Count == 0)
      return;

    var lineHeight = font.Size * 1.15;
    var totalHeight = lines.Count * lineHeight;
    var startY = rect.Y + Math.Max(0, (rect.Height - totalHeight) / 2);

    foreach (var line in lines)
    {
      gfx.DrawString(
        line,
        font,
        brush,
        new XRect(rect.X, startY, rect.Width, lineHeight),
        XStringFormats.TopCenter);
      startY += lineHeight;
    }
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
    Math.Abs(valueMm - Math.Round(valueMm)) < 0.01 ? $"{valueMm:0} mm" : $"{valueMm:0.##} mm";
}
