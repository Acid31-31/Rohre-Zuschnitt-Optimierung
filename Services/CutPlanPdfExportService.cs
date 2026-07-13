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
    string? orderReference = null)
  {
    settings ??= PdfExportSettingsStore.Load();
    PdfFontBootstrap.EnsureInitialized();

    using var document = new PdfDocument();
    document.Info.Title = "Zuschnittplan Rohre";
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

    y = DrawLine(gfx, "Zuschnittplan Rohre", fontTitle, MarginPt, y, contentWidth);
    y += 4;

    if (!string.IsNullOrWhiteSpace(orderReference))
    {
      y = DrawLine(gfx, $"Auftrag: {orderReference}", fontBold, MarginPt, y, contentWidth);
      y += 4;
    }

    if (settings.ShowCreatedDate)
      y = DrawLine(gfx, $"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}", fontSmall, MarginPt, y, contentWidth);

    if (settings.ShowCreatedDate)
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
      var requiredHeight = 80
                           + (settings.ShowBarDiagram ? 220 : 0)
                           + (settings.ShowDetailedCutSequence ? bar.StockCutSteps.Count * (LineHeight + 2) + 40 : 0);
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

      y += 6;

      if (settings.ShowBarDiagram)
        y = DrawBarDiagram(gfx, bar, bar.StockLengthMm, result.KerfMm, MarginPt, y, contentWidth, settings);
      else
        y += 4;

      if (settings.ShowDetailedCutSequence)
      {
        foreach (var step in bar.StockCutSteps)
        {
          y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
          y = DrawLine(
            gfx,
            $"  {step.StepNumber}. {step.SawAngleDeg:0}° – {step.Description}",
            fontBody,
            MarginPt,
            y,
            contentWidth);
        }

        if (!string.IsNullOrWhiteSpace(bar.SawPlanSummary))
        {
          foreach (var line in bar.SawPlanSummary.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
          {
            y = EnsureSpace(document, ref page, ref gfx, ref y, LineHeight + 2, fontBody, fontHeading, fontSmall, fontTitle, fontBold, contentWidth);
            y = DrawLine(gfx, line.Trim(), fontSmall, MarginPt, y, contentWidth);
          }
        }
      }

      y += 14;
    }

    gfx.Dispose();
    document.Save(filePath);
  }

  private static double DrawBarDiagram(
    XGraphics gfx,
    CutBarPlan bar,
    double stockLengthMm,
    double kerfMm,
    double x,
    double y,
    double width,
    PdfExportSettings settings)
  {
    const double diagramHeight = 52;

    var visibleLengthMm = stockLengthMm > 0 ? stockLengthMm : bar.StockLengthMm;
    var scale = width / visibleLengthMm;
    var centerY = y + diagramHeight / 2;
    var halfH = 14;

    var stockPen = new XPen(XColors.Gray, 1);
    var wasteBrush = new XSolidBrush(XColor.FromArgb(230, 230, 230));
    var pieceBrush1 = new XSolidBrush(XColor.FromArgb(77, 163, 255));
    var pieceBrush2 = new XSolidBrush(XColor.FromArgb(34, 197, 94));
    var piecePen1 = new XPen(XColor.FromArgb(37, 99, 235), 1.2);
    var piecePen2 = new XPen(XColor.FromArgb(22, 163, 74), 1.2);
    var kerfBrush = new XSolidBrush(XColor.FromArgb(224, 82, 82));
    var sharedPen = new XPen(XColor.FromArgb(245, 158, 11), 2.5);
    var textBrush = XBrushes.Black;
    var fontLabel = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
    var fontSmall = new XFont("Segoe UI", 7, XFontStyleEx.Regular);

    var stockWidth = visibleLengthMm * scale;
    gfx.DrawRectangle(stockPen, x, y, stockWidth, diagramHeight);

    var usedWidth = Math.Min(bar.UsedMm * scale, stockWidth);
    if (stockWidth - usedWidth > 1)
      gfx.DrawRectangle(wasteBrush, x + usedWidth, y, stockWidth - usedWidth, diagramHeight);

    if (bar.WasteMm > 0 && stockWidth - usedWidth > 12)
    {
      DrawCenteredMultilineText(
        gfx,
        ["Verschnitt", $"{bar.WasteMm:0} mm"],
        fontSmall,
        textBrush,
        new XRect(x + usedWidth, y + diagramHeight - 18, stockWidth - usedWidth, 16));
    }

    gfx.DrawString(
      "0 mm",
      fontSmall,
      textBrush,
      new XRect(x, y + diagramHeight + 2, 40, 10),
      XStringFormats.TopLeft);
    gfx.DrawString(
      $"{visibleLengthMm:0} mm",
      fontSmall,
      textBrush,
      new XRect(x + stockWidth - 48, y + diagramHeight + 2, 48, 10),
      XStringFormats.TopRight);

    var oriented = bar.OrientedPieces.Count > 0
      ? bar.OrientedPieces.ToList()
      : MiterPairingService.OrientPiecesOnBar(bar.Pieces);
    var cutAngles = MiterPairingService.BuildCutAnglesOnBar(oriented).ToList();

    var cursorX = x;
    for (var i = 0; i < oriented.Count; i++)
    {
      var piece = oriented[i];
      var pieceWidth = piece.LengthMm * scale;
      var sharedLeft = i > 0 && MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i);
      var sharedRight = i < oriented.Count - 1 && MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i + 1);

      var fill = i % 2 == 0 ? pieceBrush1 : pieceBrush2;
      var pen = i % 2 == 0 ? piecePen1 : piecePen2;
      DrawTrapezoid(gfx, cursorX, cursorX + pieceWidth, centerY, halfH, piece.MiterLeftDeg, piece.MiterRightDeg, sharedLeft, fill, pen);

      var nameLine = string.IsNullOrWhiteSpace(piece.DrawingName)
        ? $"Rohr {i + 1}"
        : $"Rohr {i + 1}";
      var lengthLine = $"{piece.LengthMm:0} mm";

      if (pieceWidth >= 28)
      {
        DrawCenteredMultilineText(
          gfx,
          [nameLine, lengthLine],
          fontLabel,
          textBrush,
          new XRect(cursorX, y + 2, pieceWidth, diagramHeight - 4));
      }
      else
      {
        DrawCenteredMultilineText(
          gfx,
          [$"{i + 1}: {piece.LengthMm:0}"],
          fontSmall,
          textBrush,
          new XRect(cursorX - 4, y + diagramHeight + 2, pieceWidth + 8, 10));
      }

      if (sharedRight)
      {
        var offset = halfH * Math.Tan(ToRadians(cutAngles[i + 1]));
        gfx.DrawLine(sharedPen, cursorX + pieceWidth, centerY + halfH, cursorX + pieceWidth - offset, centerY - halfH);
        DrawCenteredMultilineText(
          gfx,
          [$"{cutAngles[i + 1]:0}°", "gem."],
          fontSmall,
          new XSolidBrush(XColor.FromArgb(245, 158, 11)),
          new XRect(cursorX + pieceWidth - offset - 22, centerY - 12, offset + 44, 24));
      }

      cursorX += pieceWidth;

      if (i < oriented.Count - 1 && !MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i + 1) && kerfMm > 0)
      {
        var kerfWidth = Math.Max(kerfMm * scale, 2);
        gfx.DrawRectangle(kerfBrush, cursorX, y, kerfWidth, diagramHeight);
        cursorX += kerfWidth;
      }
    }

    if (oriented.Count > 0 && settings.ShowDiagramCutSequenceLine)
    {
      var angles = cutAngles.Count > 0 ? string.Join(" · ", cutAngles.Select(a => $"{a:0}°")) : "90°";
      gfx.DrawString(
        $"Schnittfolge: {angles}",
        fontSmall,
        textBrush,
        new XRect(x, y + diagramHeight + 14, width, 12),
        XStringFormats.TopLeft);
    }

    return y + diagramHeight + (settings.ShowDiagramCutSequenceLine ? 28 : 16);
  }

  private static void DrawTrapezoid(
    XGraphics gfx,
    double xStart,
    double xEnd,
    double centerY,
    double halfH,
    double leftDeg,
    double rightDeg,
    bool sharedLeft,
    XBrush fill,
    XPen pen)
  {
    var yTop = centerY - halfH;
    var yBottom = centerY + halfH;
    var leftOffset = halfH * Math.Tan(ToRadians(leftDeg));
    var rightOffset = halfH * Math.Tan(ToRadians(rightDeg));
    var leftTopX = leftDeg > 0.1 && sharedLeft ? xStart - leftOffset : xStart + leftOffset;

    var points = new[]
    {
      new XPoint(xStart, yBottom),
      new XPoint(leftTopX, yTop),
      new XPoint(xEnd - rightOffset, yTop),
      new XPoint(xEnd, yBottom)
    };

    gfx.DrawPolygon(pen, fill, points, XFillMode.Alternate);
  }

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
    var lines = WrapText(text, font, gfx, maxWidth);
    foreach (var line in lines)
    {
      gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, y));
      y += LineHeight;
    }

    return y;
  }

  private static void DrawCenteredText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
  {
    DrawCenteredMultilineText(gfx, [text], font, brush, rect);
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

  private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
