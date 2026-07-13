using System.Windows;
using System.Windows.Media;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung.Controls;

public sealed class PipeCutBarDiagram : FrameworkElement
{
  public static readonly DependencyProperty BarPlanProperty =
    DependencyProperty.Register(
      nameof(BarPlan),
      typeof(CutBarPlan),
      typeof(PipeCutBarDiagram),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

  public static readonly DependencyProperty StockLengthMmProperty =
    DependencyProperty.Register(
      nameof(StockLengthMm),
      typeof(double),
      typeof(PipeCutBarDiagram),
      new FrameworkPropertyMetadata(6000d, FrameworkPropertyMetadataOptions.AffectsRender));

  public static readonly DependencyProperty KerfMmProperty =
    DependencyProperty.Register(
      nameof(KerfMm),
      typeof(double),
      typeof(PipeCutBarDiagram),
      new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

  public CutBarPlan? BarPlan
  {
    get => (CutBarPlan?)GetValue(BarPlanProperty);
    set => SetValue(BarPlanProperty, value);
  }

  public double StockLengthMm
  {
    get => (double)GetValue(StockLengthMmProperty);
    set => SetValue(StockLengthMmProperty, value);
  }

  public double KerfMm
  {
    get => (double)GetValue(KerfMmProperty);
    set => SetValue(KerfMmProperty, value);
  }

  public PipeCutBarDiagram()
  {
    SizeChanged += (_, _) => InvalidateVisual();
  }

  protected override Size MeasureOverride(Size availableSize) =>
    new(double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width, 168);

  protected override void OnRender(DrawingContext dc)
  {
    if (BarPlan is null || StockLengthMm <= 0)
      return;

    var width = Math.Max(ActualWidth, 300);
    const double marginLeft = 8;
    const double marginRight = 8;
    const double top = 38;
    const double pipeHeight = 42;
    const double rulerY = top + pipeHeight + 34;

    var drawableWidth = width - marginLeft - marginRight;
    var visibleLengthMm = BarPlan.StockLengthMm > 0 ? BarPlan.StockLengthMm : StockLengthMm;
    var scale = drawableWidth / visibleLengthMm;
    var centerY = top + pipeHeight / 2;
    var halfH = pipeHeight / 2;
    var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

    var stockOutline = GetBrush("DiagramStockOutlineBrush", "#666666");
    var wasteFill = GetBrush("DiagramWasteBrush", "#4A4A4A");
    var pieceFill = GetBrush("DiagramPieceBrush", "#4DA3FF");
    var pieceFillAlt = GetBrush("DiagramPieceAltBrush", "#22C55E");
    var pieceBorder = GetBrush("DiagramPieceBorderBrush", "#2563EB");
    var pieceBorderAlt = GetBrush("DiagramPieceAltBrush", "#16A34A");
    var kerfFill = GetBrush("DiagramKerfBrush", "#E05252");
    var labelBrush = GetBrush("TextPrimaryBrush", "#E8E8E8");
    var mutedBrush = GetBrush("TextMutedBrush", "#9A9A9A");
    var assemblyBrush = GetBrush("DiagramAssemblyMiterBrush", "#F59E0B");

    var visibleStockWidth = visibleLengthMm * scale;
    DrawPipeBody(dc, marginLeft, top, visibleStockWidth, pipeHeight, stockOutline, wasteFill, scale, BarPlan.UsedMm);

    var oriented = BarPlan.OrientedPieces.Count > 0
      ? BarPlan.OrientedPieces.ToList()
      : MiterPairingService.OrientPiecesOnBar(BarPlan.Pieces);

    var cutAngles = MiterPairingService.BuildCutAnglesOnBar(oriented).ToList();
    var x = marginLeft;
    var sharedCuts = new List<(double X, double Angle)>();

    for (var i = 0; i < oriented.Count; i++)
    {
      var piece = oriented[i];
      var pieceWidth = piece.LengthMm * scale;
      var sharedLeft = i > 0 && MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i);
      var sharedRight = i < oriented.Count - 1 && MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i + 1);

      var fill = i % 2 == 0 ? pieceFill : pieceFillAlt;
      var border = i % 2 == 0 ? pieceBorder : pieceBorderAlt;
      var geometry = CreatePieceGeometry(
        x,
        x + pieceWidth,
        centerY,
        halfH,
        piece.MiterLeftDeg,
        piece.MiterRightDeg,
        sharedLeft);

      dc.DrawGeometry(fill, new Pen(border, 1.6), geometry);
      DrawPieceHighlight(dc, geometry, border);

      if (i == 0)
        DrawEndCutMarker(dc, x, centerY, halfH, cutAngles.FirstOrDefault(90), labelBrush, pixelsPerDip, isStart: true);

      if (sharedRight)
        sharedCuts.Add((x + pieceWidth, cutAngles[i + 1]));

      var lengthLabel = string.IsNullOrWhiteSpace(piece.DrawingName)
        ? $"Rohr {i + 1}\n{piece.LengthMm:0} mm"
        : $"Rohr {i + 1} · {piece.DrawingName}\n{piece.LengthMm:0} mm";

      DrawCenteredText(dc, lengthLabel, new Rect(x, top + 2, pieceWidth, 24), labelBrush, 10, pixelsPerDip);

      x += pieceWidth;

      if (i < oriented.Count - 1 && !MiterPairingService.IsSharedJunctionCut(oriented, cutAngles, i + 1) && KerfMm > 0)
      {
        var kerfWidth = Math.Max(KerfMm * scale, 2);
        dc.DrawRectangle(kerfFill, null, new Rect(x, top, kerfWidth, pipeHeight));
        DrawCenteredText(
          dc,
          $"{KerfMm:0} mm",
          new Rect(x - 4, top + pipeHeight / 2 - 8, kerfWidth + 8, 16),
          kerfFill,
          7,
          pixelsPerDip);
        x += kerfWidth;
      }
    }

    foreach (var (cutX, angle) in sharedCuts)
      DrawSharedCutLine(dc, cutX, centerY, halfH, angle, assemblyBrush, pixelsPerDip);

    if (oriented.Count > 0)
    {
      DrawEndCutMarker(
        dc,
        x,
        centerY,
        halfH,
        cutAngles.LastOrDefault(90),
        labelBrush,
        pixelsPerDip,
        isStart: false);
    }

    var wasteWidth = Math.Max((visibleLengthMm - BarPlan.UsedMm) * scale, 0);
    if (wasteWidth > 8)
    {
      DrawCenteredText(
        dc,
        $"Verschnitt\n{BarPlan.WasteMm:0} mm",
        new Rect(marginLeft + BarPlan.UsedMm * scale, top + pipeHeight + 4, wasteWidth, 22),
        mutedBrush,
        9,
        pixelsPerDip);
    }

    DrawRuler(dc, marginLeft, visibleStockWidth, rulerY, visibleLengthMm, mutedBrush, scale, pixelsPerDip, BarPlan.UsedMm);

    var middleInfo = cutAngles.Count > 0
      ? string.Join(" · ", cutAngles.Select(a => $"{a:0}°"))
      : "90°";

    DrawCenteredText(
      dc,
      $"Gesamtlänge {visibleLengthMm:0} mm · Schnittfolge: {middleInfo} · Säge {BarPlan.SawAdjustments}× verstellen",
      new Rect(marginLeft, 8, drawableWidth, 18),
      mutedBrush,
      10,
      pixelsPerDip);
  }

  private static void DrawPieceHighlight(DrawingContext dc, Geometry geometry, Brush outline)
  {
    var highlightPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.8);
    dc.DrawGeometry(null, highlightPen, geometry);
  }

  private static void DrawSharedCutLine(
    DrawingContext dc,
    double x,
    double centerY,
    double halfH,
    double angleDeg,
    Brush brush,
    double pixelsPerDip)
  {
    var offset = halfH * Math.Tan(ToRadians(angleDeg));
    var yTop = centerY - halfH;
    var yBottom = centerY + halfH;

    var cutPen = new Pen(brush, 3);
    dc.DrawLine(cutPen, new Point(x, yBottom), new Point(x - offset, yTop));

    DrawCenteredText(
      dc,
      $"{angleDeg:0}°\ngemeinsam",
      new Rect(x - offset - 28, centerY - 18, offset + 56, 36),
      brush,
      9,
      pixelsPerDip);
  }

  private static void DrawEndCutMarker(
    DrawingContext dc,
    double x,
    double centerY,
    double halfH,
    double angleDeg,
    Brush brush,
    double pixelsPerDip,
    bool isStart)
  {
    var yTop = centerY - halfH;
    var yBottom = centerY + halfH;
    var pen = new Pen(brush, 1.5);
    dc.DrawLine(pen, new Point(x, yTop - 4), new Point(x, yBottom + 4));

    var label = new FormattedText(
      $"{angleDeg:0}°",
      System.Globalization.CultureInfo.CurrentCulture,
      FlowDirection.LeftToRight,
      new Typeface("Segoe UI Semibold"),
      8,
      brush,
      pixelsPerDip);

    var drawX = isStart ? x + 3 : x - label.Width - 3;
    dc.DrawText(label, new Point(drawX, yTop - label.Height - 2));
  }

  private static void DrawPipeBody(
    DrawingContext dc,
    double x,
    double y,
    double width,
    double height,
    Brush outline,
    Brush wasteFill,
    double scale,
    double usedMm)
  {
    var rect = new Rect(x, y, width, height);
    dc.DrawRoundedRectangle(null, new Pen(outline, 1.5), rect, 4, 4);

    var usedWidth = Math.Min(usedMm * scale, width);
    if (usedWidth > 0)
      dc.DrawLine(new Pen(outline, 0.8), new Point(x, y + height / 2), new Point(x + usedWidth, y + height / 2));

    var wasteWidth = width - usedWidth;
    if (wasteWidth > 1)
      dc.DrawRectangle(wasteFill, null, new Rect(x + usedWidth, y, wasteWidth, height));
  }

  private static Geometry CreatePieceGeometry(
    double xStart,
    double xEnd,
    double centerY,
    double halfH,
    double leftAssemblyMiterDeg,
    double rightAssemblyMiterDeg,
    bool sharedLeftEdge = false)
  {
    var yTop = centerY - halfH;
    var yBottom = centerY + halfH;
    var leftOffset = halfH * Math.Tan(ToRadians(leftAssemblyMiterDeg));
    var rightOffset = halfH * Math.Tan(ToRadians(rightAssemblyMiterDeg));

    var leftTopX = leftAssemblyMiterDeg > 0.1 && sharedLeftEdge
      ? xStart - leftOffset
      : xStart + leftOffset;

    var figure = new PathFigure
    {
      StartPoint = new Point(xStart, yBottom),
      IsClosed = true
    };

    figure.Segments.Add(new LineSegment(new Point(leftTopX, yTop), true));
    figure.Segments.Add(new LineSegment(new Point(xEnd - rightOffset, yTop), true));
    figure.Segments.Add(new LineSegment(new Point(xEnd, yBottom), true));

    return new PathGeometry(new[] { figure });
  }

  private static void DrawMiterLabel(
    DrawingContext dc,
    double x,
    double y,
    double angleDeg,
    Brush brush,
    double pixelsPerDip,
    bool isLeft)
  {
    var text = new FormattedText(
      $"{angleDeg:0}°",
      System.Globalization.CultureInfo.CurrentCulture,
      FlowDirection.LeftToRight,
      new Typeface("Segoe UI Semibold"),
      9,
      brush,
      pixelsPerDip);

    var drawX = isLeft ? x + 2 : x - text.Width - 2;
    dc.DrawText(text, new Point(drawX, y - text.Height));
  }

  private static void DrawCenteredText(
    DrawingContext dc,
    string text,
    Rect bounds,
    Brush brush,
    double fontSize,
    double pixelsPerDip)
  {
    var formatted = new FormattedText(
      text,
      System.Globalization.CultureInfo.CurrentCulture,
      FlowDirection.LeftToRight,
      new Typeface("Segoe UI"),
      fontSize,
      brush,
      pixelsPerDip)
    {
      MaxTextWidth = Math.Max(bounds.Width, 20),
      TextAlignment = TextAlignment.Center
    };

    dc.DrawText(
      formatted,
      new Point(
        bounds.Left + (bounds.Width - formatted.Width) / 2,
        bounds.Top + (bounds.Height - formatted.Height) / 2));
  }

  private static void DrawRuler(
    DrawingContext dc,
    double startX,
    double width,
    double y,
    double visibleLengthMm,
    Brush brush,
    double scale,
    double pixelsPerDip,
    double usedMm)
  {
    dc.DrawLine(new Pen(brush, 1), new Point(startX, y), new Point(startX + width, y));

    var marks = new List<double> { 0, usedMm, visibleLengthMm }
      .Where(m => m >= 0 && m <= visibleLengthMm + 0.01)
      .Distinct()
      .OrderBy(m => m)
      .ToList();

    foreach (var mark in marks)
    {
      var x = startX + mark * scale;
      dc.DrawLine(new Pen(brush, 1), new Point(x, y - 4), new Point(x, y + 4));

      var labelText = Math.Abs(mark - usedMm) < 0.01 && mark > 0.01
        ? $"{mark:0} mm (genutzt)"
        : $"{mark:0} mm";

      var label = new FormattedText(
        labelText,
        System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI"),
        9,
        brush,
        pixelsPerDip);

      dc.DrawText(label, new Point(x - label.Width / 2, y + 6));
    }
  }

  private Brush GetBrush(string resourceKey, string fallbackHex)
  {
    if (TryFindResource(resourceKey) is Brush brush)
      return brush;

    if (Application.Current?.TryFindResource(resourceKey) is Brush appBrush)
      return appBrush;

    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
  }

  private static double ClampMiter(double angle) => Math.Clamp(angle, 0, 89.9);

  private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
