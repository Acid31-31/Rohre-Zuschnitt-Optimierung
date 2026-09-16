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
    const double top = 36;
    const double pipeHeight = 42;
    const double rulerY = top + pipeHeight + 34;

    var drawableWidth = width - marginLeft - marginRight;
    var visibleLengthMm = BarPlan.StockLengthMm > 0 ? BarPlan.StockLengthMm : StockLengthMm;
    var scale = drawableWidth / visibleLengthMm;
    var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

    var wasteFill = GetBrush("DiagramWasteBrush", "#4A4A4A");
    var labelBrush = GetBrush("TextPrimaryBrush", "#E8E8E8");
    var mutedBrush = GetBrush("TextMutedBrush", "#9A9A9A");
    var cutLineBrush = new SolidColorBrush(Colors.Black);

    var visibleStockWidth = visibleLengthMm * scale;
    var usedWidth = Math.Min(BarPlan.UsedMm * scale, visibleStockWidth);
    if (visibleStockWidth - usedWidth > 1)
      dc.DrawRectangle(wasteFill, null, new Rect(marginLeft + usedWidth, top, visibleStockWidth - usedWidth, pipeHeight));

    var oriented = BarPlan.OrientedPieces.Count > 0
      ? BarPlan.OrientedPieces.ToList()
      : MiterPairingService.OrientPiecesOnBar(BarPlan.Pieces);
    var cutAngles = MiterPairingService.BuildCutAnglesOnBar(oriented).ToList();
    var gehrungIndex = MiterPairingService.GehrungColorIndex(oriented);
    var lefts = new double[oriented.Count];
    var rights = new double[oriented.Count];
    var x = marginLeft;
    var cutPen = new Pen(cutLineBrush, 2);

    for (var i = 0; i < oriented.Count; i++)
    {
      var piece = oriented[i];
      var pieceWidth = Math.Max(piece.LengthMm * scale, 1);
      lefts[i] = x;
      rights[i] = x + pieceWidth;
      var (fill, _) = BrushForGehrung(MiterPairingService.PrimaryMiterDeg(piece), gehrungIndex);
      dc.DrawRectangle(fill, null, new Rect(x, top, pieceWidth, pipeHeight));

      if (pieceWidth >= 40)
      {
        DrawCenteredText(
          dc,
          $"{i + 1}\n{piece.LengthMm:0} mm",
          new Rect(x, top + 8, pieceWidth, 26),
          labelBrush,
          10,
          pixelsPerDip);
      }
      else if (pieceWidth >= 14)
      {
        DrawCenteredText(dc, $"{i + 1}", new Rect(x, top + 12, pieceWidth, 16), labelBrush, 9, pixelsPerDip);
      }

      x += pieceWidth;
    }

    if (oriented.Count > 0)
    {
      DrawSawCut(dc, cutPen, lefts[0], top, pipeHeight, cutAngles[0]);
      for (var i = 0; i < oriented.Count - 1; i++)
        DrawSawCut(dc, cutPen, rights[i], top, pipeHeight, cutAngles[i + 1]);
      DrawSawCut(dc, cutPen, rights[^1], top, pipeHeight, cutAngles[^1]);
    }

    foreach (var run in GehrungRuns(oriented, lefts, rights))
    {
      DrawCenteredText(
        dc,
        $"{run.Angle:0}°",
        new Rect(run.Left, top - 18, Math.Max(run.Right - run.Left, 20), 16),
        labelBrush,
        11,
        pixelsPerDip);
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

    DrawCenteredText(
      dc,
      $"Gesamtlänge {visibleLengthMm:0} mm · Farbe = Gehrung · Rechteck = Rohr · Schräge = ein Schnitt · Säge {BarPlan.SawAdjustments}× verstellen",
      new Rect(marginLeft, 4, drawableWidth, 16),
      mutedBrush,
      10,
      pixelsPerDip);
  }

  private static bool IsMiterAngle(double deg) => deg > 0.5 && Math.Abs(deg - 90) > 0.5;

  private static void DrawSawCut(DrawingContext dc, Pen pen, double x, double top, double height, double angleDeg)
  {
    if (IsMiterAngle(angleDeg))
    {
      DrawMiterSlash(dc, pen, x, top, height, angleDeg);
      return;
    }

    dc.DrawLine(pen, new Point(x, top), new Point(x, top + height));
  }

  private static void DrawMiterSlash(DrawingContext dc, Pen pen, double x, double top, double height, double angleDeg)
  {
    var offset = Math.Min(14, height * Math.Tan(Math.Min(angleDeg, 60) * Math.PI / 180d) / 2);
    dc.DrawLine(pen, new Point(x, top + height), new Point(x - offset, top));
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

  private static (Brush Fill, Brush Border) BrushForGehrung(
    double miterDeg,
    IReadOnlyDictionary<double, int> gehrungIndex)
  {
    if (miterDeg <= 0.1 || Math.Abs(miterDeg - 90) < 0.1)
      return (new SolidColorBrush(Color.FromRgb(0xD2, 0xD2, 0xD2)), new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));

    var index = gehrungIndex.TryGetValue(miterDeg, out var mapped) ? mapped : 0;
    return index switch
    {
      0 => (new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)), new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))),
      1 => (new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)), new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))),
      2 => (new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), new SolidColorBrush(Color.FromRgb(0xB4, 0x64, 0x0A))),
      _ => (new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)), new SolidColorBrush(Color.FromRgb(0x6E, 0x32, 0xB4)))
    };
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
}
