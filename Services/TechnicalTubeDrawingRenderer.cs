using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Werkstattzeichnung im Lieferantenformat (A3 quer, ISO 5457 Rahmen, Schriftfeld wie Tesla-Automation).
/// Logo: eigenes App-Icon statt Fremdlogo.
/// </summary>
internal static class TechnicalTubeDrawingRenderer
{
  private const double MarginLeftMm = 20;
  private const double MarginOtherMm = 10;
  private const double FrameLineMm = 0.7;
  private const double GridFieldMm = 50;
  private const double TitleBlockWMm = 248;
  private const double TitleBlockHMm = 78;

  public static void Render(
    string filePath,
    string drawingNumber,
    PipeProfileDefinition? profile,
    string? material,
    double lengthMm,
    int quantity,
    double miterEnd1Deg,
    double miterEnd2Deg,
    string? orderReference)
  {
    PdfFontBootstrap.EnsureInitialized();

    using var document = new PdfDocument();
    document.Info.Title = drawingNumber;
    document.Info.Author = AppInfo.ProductName;

    var page = document.AddPage();
    page.Size = PdfSharp.PageSize.A3;
    page.Orientation = PdfSharp.PageOrientation.Landscape;

    var gfx = XGraphics.FromPdfPage(page);
    var pageW = page.Width.Point;
    var pageH = page.Height.Point;

    var fontGrid = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
    var fontLabel = new XFont("Segoe UI", 5.2, XFontStyleEx.Regular);
    var fontValue = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
    var fontTitle = new XFont("Segoe UI", 11, XFontStyleEx.Bold);
    var fontSmall = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
    var fontTiny = new XFont("Segoe UI", 5, XFontStyleEx.Regular);
    var fontDim = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
    var fontView = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
    var fontCompany = new XFont("Segoe UI", 12, XFontStyleEx.Bold);

    var frameX = Mm(MarginLeftMm);
    var frameY = Mm(MarginOtherMm);
    var frameW = pageW - Mm(MarginLeftMm + MarginOtherMm);
    var frameH = pageH - Mm(MarginOtherMm * 2);
    var framePen = new XPen(XColors.Black, Mm(FrameLineMm));
    gfx.DrawRectangle(framePen, frameX, frameY, frameW, frameH);

    DrawIso5457Grid(gfx, frameX, frameY, frameW, frameH, fontGrid);
    DrawCentringMarks(gfx, frameX, frameY, frameW, frameH, framePen);
    gfx.DrawString("A3", fontTiny, XBrushes.DimGray, new XPoint(frameX + frameW - Mm(8), frameY + frameH + Mm(3)));

    var titleW = Mm(TitleBlockWMm);
    var titleH = Mm(TitleBlockHMm);
    var titleX = frameX + frameW - titleW;
    var titleY = frameY + frameH - titleH;

    var drawingX = frameX + Mm(8);
    var drawingY = frameY + Mm(8);
    var drawingW = frameW - Mm(16);
    var drawingH = frameH - titleH - Mm(12);

    var dims = ProfileDimensions.Parse(profile);

    gfx.DrawString("ALLE MASSE IN mm  ·  SCHEMATISCH, NICHT MASSSTABSGETREU", fontTiny, XBrushes.DimGray, new XPoint(drawingX, drawingY + Mm(3)));

    var sideH = drawingH * 0.42;
    DrawViewLabel(gfx, drawingX, drawingY + Mm(8), "ANSICHT A", fontView);
    DrawSideElevation(
      gfx,
      drawingX,
      drawingY + Mm(16),
      drawingW * 0.72,
      sideH,
      profile,
      dims,
      lengthMm,
      miterEnd1Deg,
      miterEnd2Deg,
      fontDim,
      fontSmall);

    var rightX = drawingX + drawingW * 0.74;
    var rightW = drawingW * 0.26;
    DrawViewLabel(gfx, rightX, drawingY + Mm(8), "SCHNITT B-B", fontView);
    DrawSectionView(
      gfx,
      rightX,
      drawingY + Mm(16),
      rightW,
      sideH,
      profile,
      dims,
      fontDim,
      fontSmall);

    var lowerY = drawingY + sideH + Mm(22);
    var lowerH = drawingH - sideH - Mm(28);
    DrawViewLabel(gfx, drawingX, lowerY, "ENDE A", fontView);
    DrawEndView(
      gfx,
      drawingX,
      lowerY + Mm(8),
      drawingW * 0.28,
      lowerH - Mm(8),
      miterEnd1Deg,
      fontSmall);

    DrawNotes(
      gfx,
      drawingX + drawingW * 0.32,
      lowerY + Mm(8),
      drawingW * 0.38,
      lowerH - Mm(8),
      quantity,
      miterEnd1Deg,
      miterEnd2Deg,
      fontSmall,
      fontTiny);

    DrawTeslaStyleTitleBlock(
      gfx,
      titleX,
      titleY,
      titleW,
      titleH,
      drawingNumber,
      orderReference,
      profile,
      material,
      lengthMm,
      quantity,
      miterEnd1Deg,
      miterEnd2Deg,
      fontLabel,
      fontValue,
      fontTitle,
      fontTiny,
      fontCompany);

    document.Save(filePath);
  }

  private static void DrawIso5457Grid(XGraphics gfx, double frameX, double frameY, double frameW, double frameH, XFont font)
  {
    var colCount = 8;
    var rowCount = 6;
    var letters = BuildGridLetters(rowCount);

    var colW = frameW / colCount;
    for (var i = 0; i < colCount; i++)
    {
      var cx = frameX + colW * i + colW / 2;
      var label = (i + 1).ToString(CultureInfo.InvariantCulture);
      gfx.DrawString(label, font, XBrushes.DimGray, new XRect(cx - Mm(6), frameY - Mm(8), Mm(12), Mm(6)), XStringFormats.Center);
      gfx.DrawString(label, font, XBrushes.DimGray, new XRect(cx - Mm(6), frameY + frameH + Mm(1), Mm(12), Mm(6)), XStringFormats.Center);
      if (i > 0)
        gfx.DrawLine(new XPen(XColors.LightGray, 0.25), frameX + colW * i, frameY, frameX + colW * i, frameY + Mm(3));
    }

    var rowH = frameH / rowCount;
    for (var i = 0; i < letters.Length; i++)
    {
      var cy = frameY + rowH * i + rowH / 2;
      gfx.DrawString(letters[i], font, XBrushes.DimGray, new XRect(frameX - Mm(8), cy - Mm(4), Mm(6), Mm(8)), XStringFormats.Center);
      gfx.DrawString(letters[i], font, XBrushes.DimGray, new XRect(frameX + frameW + Mm(1), cy - Mm(4), Mm(7), Mm(8)), XStringFormats.Center);
    }
  }

  private static string[] BuildGridLetters(int count)
  {
    var result = new List<string>();
    for (var code = 'A'; result.Count < count && code <= 'Z'; code++)
    {
      if (code is 'I' or 'O')
        continue;
      result.Add(code.ToString(CultureInfo.InvariantCulture));
    }

    return result.ToArray();
  }

  private static void DrawCentringMarks(XGraphics gfx, double x, double y, double w, double h, XPen pen)
  {
    var ext = Mm(8);
    var midX = x + w / 2;
    var midY = y + h / 2;
    gfx.DrawLine(pen, x - ext, midY, x, midY);
    gfx.DrawLine(pen, x + w, midY, x + w + ext, midY);
    gfx.DrawLine(pen, midX, y - ext, midX, y);
    gfx.DrawLine(pen, midX, y + h, midX, y + h + ext);
  }

  private static void DrawViewLabel(XGraphics gfx, double x, double y, string label, XFont font)
  {
    gfx.DrawString(label, font, XBrushes.Black, new XPoint(x, y));
  }

  private static void DrawNotes(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    int quantity,
    double miterEnd1Deg,
    double miterEnd2Deg,
    XFont fontSmall,
    XFont fontTiny)
  {
    gfx.DrawRectangle(new XPen(XColors.Black, 0.45), x, y, w, h);
    gfx.DrawString("HINWEISE", fontTiny, XBrushes.DimGray, new XPoint(x + 6, y + 10));
    var lines = new[]
    {
      $"Stückzahl: {quantity} Stk",
      $"Gehrung: {ShortMiter(miterEnd1Deg, miterEnd2Deg)}",
      "Alle Maße vor dem Sägen prüfen.",
      "Gratfrei, Schnittkanten entgraten.",
      "Projektion: erster Winkel (ISO)."
    };
    for (var i = 0; i < lines.Length; i++)
      gfx.DrawString(lines[i], fontSmall, XBrushes.Black, new XPoint(x + 8, y + 24 + i * 12));
  }

  private static void DrawTeslaStyleTitleBlock(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    string drawingNumber,
    string? orderReference,
    PipeProfileDefinition? profile,
    string? material,
    double lengthMm,
    int quantity,
    double miterEnd1Deg,
    double miterEnd2Deg,
    XFont fontLabel,
    XFont fontValue,
    XFont fontTitle,
    XFont fontTiny,
    XFont fontCompany)
  {
    var pen = new XPen(XColors.Black, 0.7);
    var thin = new XPen(XColors.Black, 0.4);
    gfx.DrawRectangle(pen, x, y, w, h);

    var leftW = w * 0.22;
    var midW = w * 0.28;
    var rightW = w - leftW - midW;
    var revH = h * 0.18;
    var bodyH = h - revH;

    gfx.DrawLine(thin, x + leftW, y, x + leftW, y + bodyH);
    gfx.DrawLine(thin, x + leftW + midW, y, x + leftW + midW, y + bodyH);
    gfx.DrawLine(thin, x, y + bodyH, x + w, y + bodyH);

    DrawLabeledBox(gfx, x, y, leftW, bodyH * 0.38, "OBERFLÄCHENBEHANDLUNG", "—", fontLabel, fontValue);
    gfx.DrawLine(thin, x, y + bodyH * 0.38, x + leftW, y + bodyH * 0.38);
    DrawLabeledBox(gfx, x, y + bodyH * 0.38, leftW, bodyH * 0.62, "ALLG. TOLERANZEN", "ISO 2768-m", fontLabel, fontValue);
    gfx.DrawString("Maße ohne Toleranzangabe", fontTiny, XBrushes.DimGray, new XPoint(x + 4, y + bodyH * 0.38 + 28));

    var midX = x + leftW;
    var row = bodyH / 5;
    DrawLabeledBox(gfx, midX, y, midW * 0.45, row, "FORMAT", "A3", fontLabel, fontValue);
    DrawLabeledBox(gfx, midX + midW * 0.45, y, midW * 0.55, row, "MASSSTAB", "ohne", fontLabel, fontValue);
    gfx.DrawLine(thin, midX, y + row, midX + midW, y + row);
    gfx.DrawLine(thin, midX + midW * 0.45, y, midX + midW * 0.45, y + row);

    DrawFirstAngleProjection(gfx, midX + 8, y + row + 4, 36);
    DrawLabeledBox(gfx, midX + 48, y + row, midW - 48, row, "PROJEKTION", "1. Winkel (ISO)", fontLabel, fontValue);
    gfx.DrawLine(thin, midX, y + row * 2, midX + midW, y + row * 2);

    DrawLabeledBox(gfx, midX, y + row * 2, midW, row, "KONSTRUKTEUR", "RZO", fontLabel, fontValue);
    gfx.DrawLine(thin, midX, y + row * 3, midX + midW, y + row * 3);
    DrawLabeledBox(
      gfx,
      midX,
      y + row * 3,
      midW,
      row,
      "GEZEICHNET VON",
      DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      fontLabel,
      fontValue);
    gfx.DrawLine(thin, midX, y + row * 4, midX + midW, y + row * 4);
    DrawLabeledBox(gfx, midX, y + row * 4, midW, bodyH - row * 4, "GEPRÜFT DURCH", "—", fontLabel, fontValue);

    var rightX = x + leftW + midW;
    var nameH = bodyH * 0.24;
    var metaH = bodyH * 0.16;
    var logoH = bodyH - nameH - metaH;

    gfx.DrawLine(thin, rightX, y + nameH, rightX + rightW, y + nameH);
    gfx.DrawLine(thin, rightX, y + nameH + metaH, rightX + rightW, y + nameH + metaH);
    gfx.DrawLine(thin, rightX + rightW * 0.62, y, rightX + rightW * 0.62, y + nameH + metaH);
    gfx.DrawLine(thin, rightX + rightW * 0.82, y, rightX + rightW * 0.82, y + nameH);

    DrawLabeledBox(gfx, rightX, y, rightW * 0.62, nameH, "BAUTEIL BENENNUNG", Trim(profile?.KindLabel ?? "Rohr", 22), fontLabel, fontTitle);
    DrawLabeledBox(gfx, rightX + rightW * 0.62, y, rightW * 0.20, nameH, "ZEICHNUNGSNUMMER", drawingNumber, fontLabel, fontValue);
    DrawLabeledBox(gfx, rightX + rightW * 0.82, y, rightW * 0.18, nameH, "REV", "00", fontLabel, fontTitle);

    DrawLabeledBox(gfx, rightX, y + nameH, rightW * 0.62, metaH, "MAT.", Trim(material ?? "—", 28), fontLabel, fontValue);
    DrawLabeledBox(gfx, rightX + rightW * 0.62, y + nameH, rightW * 0.38, metaH, "LÄNGE / STK", $"{FormatMm(lengthMm)} · {quantity} Stk", fontLabel, fontValue);

    var colRev = w / 5;
    gfx.DrawLine(thin, x + colRev, y + bodyH, x + colRev, y + h);
    gfx.DrawLine(thin, x + colRev * 2, y + bodyH, x + colRev * 2, y + h);
    gfx.DrawLine(thin, x + colRev * 3, y + bodyH, x + colRev * 3, y + h);
    gfx.DrawLine(thin, x + colRev * 4, y + bodyH, x + colRev * 4, y + h);

    DrawCompanyBlock(gfx, rightX, y + nameH + metaH, rightW, logoH, fontCompany, fontTiny);

    DrawLabeledBox(gfx, x, y + bodyH, colRev, revH, "REV", "00", fontLabel, fontValue);
    DrawLabeledBox(gfx, x + colRev, y + bodyH, colRev, revH, "DATUM", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fontLabel, fontValue);
    DrawLabeledBox(gfx, x + colRev * 2, y + bodyH, colRev, revH, "NAME", "RZO", fontLabel, fontValue);
    DrawLabeledBox(gfx, x + colRev * 3, y + bodyH, colRev, revH, "ÄNDERUNGSINDEX", "00", fontLabel, fontValue);
    DrawLabeledBox(gfx, x + colRev * 4, y + bodyH, colRev, revH, "BESCHREIBUNG", "Erstausgabe", fontLabel, fontValue);
  }

  private const string CompanyMainText = "ROHRE ZUSCHNITT";
  private const string CompanyTagText = "CUT. PLAN. DONE.";
  private const double CompanyTagSizeRatio = 0.48;
  private const double CompanyLineGapRatio = 0.10;

  private readonly struct CompanyTextLayout
  {
    public XFont MainFont { get; init; }
    public XFont TagFont { get; init; }
    public double MainHeight { get; init; }
    public double TagHeight { get; init; }
    public double Gap { get; init; }
  }

  private static CompanyTextLayout FitCompanyText(XGraphics gfx, double maxW, double maxH)
  {
    var fallbackMain = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
    var fallbackTag = new XFont("Segoe UI", 4, XFontStyleEx.Bold);
    var fallback = new CompanyTextLayout
    {
      MainFont = fallbackMain,
      TagFont = fallbackTag,
      MainHeight = gfx.MeasureString(CompanyMainText, fallbackMain).Height,
      TagHeight = gfx.MeasureString(CompanyTagText, fallbackTag).Height,
      Gap = Mm(0.3)
    };

    for (var mainPt = 48.0; mainPt >= 6.0; mainPt -= 0.25)
    {
      var tagPt = mainPt * CompanyTagSizeRatio;
      var mainFont = new XFont("Segoe UI", mainPt, XFontStyleEx.Bold);
      var tagFont = new XFont("Segoe UI", tagPt, XFontStyleEx.Bold);
      var mainSize = gfx.MeasureString(CompanyMainText, mainFont);
      var tagSize = gfx.MeasureString(CompanyTagText, tagFont);
      var gap = mainPt * CompanyLineGapRatio;
      var totalH = mainSize.Height + gap + tagSize.Height;

      if (mainSize.Width <= maxW && tagSize.Width <= maxW && totalH <= maxH)
      {
        return new CompanyTextLayout
        {
          MainFont = mainFont,
          TagFont = tagFont,
          MainHeight = mainSize.Height,
          TagHeight = tagSize.Height,
          Gap = gap
        };
      }
    }

    return fallback;
  }

  private static void DrawCompanyBlock(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    XFont fontCompany,
    XFont fontTiny)
  {
    var leftPad = Mm(0.8);
    var logoSide = h;
    var logoX = x + leftPad;
    var logoY = y;
    DrawAppLogo(gfx, logoX, logoY, logoSide);

    var textX = logoX + logoSide + Mm(2.5);
    var textPadH = Mm(1.0);
    var textPadV = Mm(1.2);
    var textAreaW = w - (textX - x) - leftPad - textPadH;
    var textAreaH = h - textPadV * 2;
    var layout = FitCompanyText(gfx, textAreaW, textAreaH);
    var textBlockH = layout.MainHeight + layout.Gap + layout.TagHeight;
    var textTop = y + textPadV + (textAreaH - textBlockH) / 2;

    gfx.DrawString(
      CompanyMainText,
      layout.MainFont,
      XBrushes.Black,
      new XRect(textX, textTop, textAreaW, layout.MainHeight),
      XStringFormats.TopLeft);
    gfx.DrawString(
      CompanyTagText,
      layout.TagFont,
      new XSolidBrush(XColor.FromArgb(0x2E, 0xB7, 0x8A)),
      new XRect(textX, textTop + layout.MainHeight + layout.Gap, textAreaW, layout.TagHeight),
      XStringFormats.TopLeft);
  }

  private static void DrawAppLogo(XGraphics gfx, double x, double y, double size)
  {
    gfx.DrawRectangle(XBrushes.Black, x, y, size, size);

    using var image = TryLoadDrawingLogo();
    if (image is not null)
    {
      gfx.DrawImage(image, x, y, size, size);
      return;
    }

    gfx.DrawString(
      "R",
      new XFont("Segoe UI", size * 0.45, XFontStyleEx.Bold),
      XBrushes.White,
      new XRect(x, y, size, size),
      XStringFormats.Center);
  }

  private static XImage? TryLoadDrawingLogo()
  {
    var bytes = LoadDrawingLogoBytes();
    if (bytes is null || bytes.Length == 0)
      return null;

    return XImage.FromStream(new MemoryStream(bytes));
  }

  private static byte[]? LoadDrawingLogoBytes()
  {
    using var resource = typeof(TechnicalTubeDrawingRenderer).Assembly
      .GetManifestResourceStream("DrawingLogo.png");
    if (resource is not null)
    {
      using var memory = new MemoryStream();
      resource.CopyTo(memory);
      return memory.ToArray();
    }

    var path = ResolveDrawingLogoPath();
    return path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;
  }

  private static string? ResolveDrawingLogoPath()
  {
    var dir = AppInfo.GetApplicationDirectory();
    foreach (var candidate in new[]
             {
               Path.Combine(dir, "DrawingLogo.png"),
               Path.Combine(dir, "Assets", "DrawingLogo.png")
             })
    {
      if (File.Exists(candidate))
        return candidate;
    }

    return null;
  }

  private static void DrawFirstAngleProjection(XGraphics gfx, double x, double y, double size)
  {
    var pen = new XPen(XColors.Black, 0.7);
    gfx.DrawEllipse(pen, x, y + 4, size * 0.38, size * 0.38);
    gfx.DrawEllipse(pen, x + 3, y + 7, size * 0.22, size * 0.22);
    var trap = new[]
    {
      new XPoint(x + size * 0.48, y + 6),
      new XPoint(x + size * 0.95, y + 2),
      new XPoint(x + size * 0.95, y + size * 0.48),
      new XPoint(x + size * 0.48, y + size * 0.38)
    };
    gfx.DrawPolygon(pen, trap);
  }

  private static void DrawLabeledBox(
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
    gfx.DrawString(label, fontLabel, XBrushes.DimGray, new XPoint(x + 4, y + 8));
    gfx.DrawString(value, fontValue, XBrushes.Black, new XRect(x + 4, y + 10, w - 8, h - 12), XStringFormats.TopLeft);
  }

  private static void DrawSideElevation(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    PipeProfileDefinition? profile,
    ProfileDimensions dims,
    double lengthMm,
    double miterEnd1Deg,
    double miterEnd2Deg,
    XFont fontDim,
    XFont fontSmall)
  {
    using var clip = new ClipScope(gfx, x, y, w, h);

    var outerH = Math.Max(28, Math.Min(48, dims.OuterHeight * 0.5));
    var tubeW = Math.Min(w - 70, Math.Max(220, w * 0.82));
    var tubeX = x + (w - tubeW) / 2;
    var tubeY = y + h * 0.38 - outerH / 2;

    var leftInset = MiterInset(outerH, miterEnd1Deg);
    var rightInset = MiterInset(outerH, miterEnd2Deg);
    var topLeft = new XPoint(tubeX + leftInset, tubeY);
    var topRight = new XPoint(tubeX + tubeW - rightInset, tubeY);
    var bottomRight = new XPoint(tubeX + tubeW, tubeY + outerH);
    var bottomLeft = new XPoint(tubeX, tubeY + outerH);

    var penThick = new XPen(XColors.Black, 1.5);
    var penThin = new XPen(XColors.Black, 0.75);
    var penDash = new XPen(XColors.Gray, 0.55) { DashStyle = XDashStyle.Dash };

    gfx.DrawLine(penDash, tubeX - 10, tubeY + outerH / 2, tubeX + tubeW + 10, tubeY + outerH / 2);
    gfx.DrawPolygon(penThick, XBrushes.White, new[] { topLeft, topRight, bottomRight, bottomLeft }, XFillMode.Alternate);

    var wall = Math.Max(2, outerH * (dims.WallMm > 0 ? dims.WallMm / Math.Max(dims.OuterHeight, 1) : 0.14));
    gfx.DrawLine(penThin, topLeft.X, topLeft.Y + wall, topRight.X, topRight.Y + wall);
    gfx.DrawLine(penThin, bottomLeft.X, bottomLeft.Y - wall, bottomRight.X, bottomRight.Y - wall);

    if (miterEnd1Deg > 0.1)
      gfx.DrawString($"A {miterEnd1Deg:0}°", fontSmall, XBrushes.Black, new XRect(tubeX, tubeY + outerH + 2, tubeW * 0.4, 12), XStringFormats.TopLeft);
    if (miterEnd2Deg > 0.1)
      gfx.DrawString($"B {miterEnd2Deg:0}°", fontSmall, XBrushes.Black, new XRect(tubeX + tubeW * 0.6, tubeY + outerH + 2, tubeW * 0.4, 12), XStringFormats.TopRight);

    var dimY = Math.Min(y + h - 14, tubeY + outerH + 26);
    if (dimY > tubeY + outerH + 12)
      DrawHorizontalDimension(gfx, tubeX, tubeX + tubeW, dimY, FormatMm(lengthMm), fontDim);

    gfx.DrawString(
      Trim(profile?.FullLabel ?? "Rohr", 42),
      fontSmall,
      XBrushes.Black,
      new XRect(x, tubeY - 16, w, 12),
      XStringFormats.Center);
  }

  private static void DrawSectionView(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    PipeProfileDefinition? profile,
    ProfileDimensions dims,
    XFont fontDim,
    XFont fontSmall)
  {
    using var clip = new ClipScope(gfx, x, y, w, h);

    var cx = x + w / 2;
    var cy = y + h * 0.42;
    var outer = Math.Min(w, h) * 0.48;
    var inner = outer * (dims.WallMm > 0 && dims.OuterWidth > 0
      ? Math.Max(0.4, 1 - 2 * dims.WallMm / dims.OuterWidth)
      : 0.65);

    if (profile?.Kind == PipeProfileKind.RoundBar)
    {
      gfx.DrawEllipse(new XPen(XColors.Black, 1.4), XBrushes.White, cx - outer / 2, cy - outer / 2, outer, outer);
      HatchCircleRing(gfx, cx, cy, outer / 2, 0);
    }
    else if (profile?.Kind == PipeProfileKind.Round)
    {
      gfx.DrawEllipse(new XPen(XColors.Black, 1.4), XBrushes.White, cx - outer / 2, cy - outer / 2, outer, outer);
      HatchCircleRing(gfx, cx, cy, outer / 2, inner / 2);
      gfx.DrawEllipse(new XPen(XColors.Black, 0.85), XBrushes.White, cx - inner / 2, cy - inner / 2, inner, inner);
    }
    else
    {
      var ow = outer;
      var oh = (profile?.Kind is PipeProfileKind.Rectangular or PipeProfileKind.CProfile or PipeProfileKind.UProfile or PipeProfileKind.TProfile) && dims.OuterHeight > 0
        ? outer * (dims.OuterHeight / Math.Max(dims.OuterWidth, 1))
        : outer;
      var iw = inner;
      var ih = (profile?.Kind is PipeProfileKind.Rectangular or PipeProfileKind.CProfile or PipeProfileKind.UProfile or PipeProfileKind.TProfile) && dims.OuterHeight > 0
        ? inner * (dims.OuterHeight / Math.Max(dims.OuterWidth, 1))
        : inner;

      var ox = cx - ow / 2;
      var oy = cy - oh / 2;
      gfx.DrawRectangle(new XPen(XColors.Black, 1.4), XBrushes.White, ox, oy, ow, oh);
      HatchRectArea(gfx, ox, oy, ow, oh);
      gfx.DrawRectangle(XBrushes.White, cx - iw / 2, cy - ih / 2, iw, ih);
      gfx.DrawRectangle(new XPen(XColors.Black, 0.85), cx - iw / 2, cy - ih / 2, iw, ih);

      var dimY = oy + oh + 18;
      if (dimY < y + h - 16)
        DrawHorizontalDimension(gfx, ox, ox + ow, dimY, FormatMm(dims.OuterWidth), fontDim);
    }

    gfx.DrawString(
      profile?.Dimensions ?? "—",
      fontSmall,
      XBrushes.Black,
      new XRect(x, y + h - 12, w, 10),
      XStringFormats.Center);
  }

  private static void DrawEndView(
    XGraphics gfx,
    double x,
    double y,
    double w,
    double h,
    double miterDeg,
    XFont fontSmall)
  {
    using var clip = new ClipScope(gfx, x, y, w, h);

    var cx = x + w / 2;
    var cy = y + h / 2 - 4;
    var size = Math.Min(w, h) * 0.55;

    if (miterDeg <= 0.1)
    {
      gfx.DrawRectangle(new XPen(XColors.Black, 1.2), cx - size / 2, cy - size / 2, size, size);
      gfx.DrawString("lotrecht", fontSmall, XBrushes.Black, new XRect(x, cy + size / 2 + 4, w, 12), XStringFormats.Center);
      return;
    }

    var inset = Math.Min(size * 0.2, size * 0.25 * Math.Tan(miterDeg * Math.PI / 180.0));
    var points = new[]
    {
      new XPoint(cx - size / 2 + inset, cy - size / 2),
      new XPoint(cx + size / 2, cy - size / 2),
      new XPoint(cx + size / 2, cy + size / 2),
      new XPoint(cx - size / 2, cy + size / 2)
    };
    gfx.DrawPolygon(new XPen(XColors.Black, 1.2), XBrushes.White, points, XFillMode.Alternate);
    gfx.DrawString(miterDeg.ToString("0", CultureInfo.InvariantCulture) + "°", fontSmall, XBrushes.Black, new XRect(x, cy + size / 2 + 4, w, 12), XStringFormats.Center);
  }

  private static void DrawHorizontalDimension(XGraphics gfx, double x1, double x2, double y, string label, XFont font)
  {
    if (x2 - x1 < 20)
      return;

    var pen = new XPen(XColors.Black, 0.7);
    gfx.DrawLine(pen, x1, y, x2, y);
    DrawArrow(gfx, x1, y, -1);
    DrawArrow(gfx, x2, y, 1);
    gfx.DrawLine(pen, x1, y - 4, x1, y + 4);
    gfx.DrawLine(pen, x2, y - 4, x2, y + 4);
    gfx.DrawString(label, font, XBrushes.Black, new XRect(x1, y - 15, x2 - x1, 12), XStringFormats.Center);
  }

  private static void DrawArrow(XGraphics gfx, double x, double y, int direction)
  {
    var size = 4.0;
    var points = direction < 0
      ? new[] { new XPoint(x, y), new XPoint(x + size, y - size * 0.6), new XPoint(x + size, y + size * 0.6) }
      : new[] { new XPoint(x, y), new XPoint(x - size, y - size * 0.6), new XPoint(x - size, y + size * 0.6) };
    gfx.DrawPolygon(XPens.Black, XBrushes.Black, points, XFillMode.Alternate);
  }

  private static void HatchRectArea(XGraphics gfx, double ox, double oy, double ow, double oh)
  {
    var pen = new XPen(XColors.Gray, 0.35);
    var step = 3.5;
    for (var d = -oh; d < ow; d += step)
      DrawClippedLine(gfx, pen, ox + d, oy + oh, ox + d + oh, oy, ox, oy, ow, oh);
  }

  private static void HatchCircleRing(XGraphics gfx, double cx, double cy, double outerR, double innerR)
  {
    var pen = new XPen(XColors.Gray, 0.35);
    var bounds = outerR * 2;
    for (var d = -bounds; d < bounds; d += 3.5)
      DrawClippedLine(gfx, pen, cx + d, cy + outerR, cx + d + bounds, cy - outerR, cx - outerR, cy - outerR, bounds, bounds);
  }

  private static void DrawClippedLine(
    XGraphics gfx,
    XPen pen,
    double x1,
    double y1,
    double x2,
    double y2,
    double clipX,
    double clipY,
    double clipW,
    double clipH)
  {
    if (ClipLine(ref x1, ref y1, ref x2, ref y2, clipX, clipY, clipX + clipW, clipY + clipH))
      gfx.DrawLine(pen, x1, y1, x2, y2);
  }

  private static bool ClipLine(
    ref double x1,
    ref double y1,
    ref double x2,
    ref double y2,
    double xmin,
    double ymin,
    double xmax,
    double ymax)
  {
    const int INSIDE = 0, LEFT = 1, RIGHT = 2, BOTTOM = 4, TOP = 8;
    int ComputeCode(double x, double y)
    {
      var code = INSIDE;
      if (x < xmin) code |= LEFT;
      else if (x > xmax) code |= RIGHT;
      if (y < ymin) code |= TOP;
      else if (y > ymax) code |= BOTTOM;
      return code;
    }

    var code1 = ComputeCode(x1, y1);
    var code2 = ComputeCode(x2, y2);
    while (true)
    {
      if ((code1 | code2) == 0)
        return true;
      if ((code1 & code2) != 0)
        return false;

      var codeOut = code1 != 0 ? code1 : code2;
      double x;
      double y;
      if ((codeOut & BOTTOM) != 0)
      {
        x = x1 + (x2 - x1) * (ymax - y1) / (y2 - y1);
        y = ymax;
      }
      else if ((codeOut & TOP) != 0)
      {
        x = x1 + (x2 - x1) * (ymin - y1) / (y2 - y1);
        y = ymin;
      }
      else if ((codeOut & RIGHT) != 0)
      {
        y = y1 + (y2 - y1) * (xmax - x1) / (x2 - x1);
        x = xmax;
      }
      else
      {
        y = y1 + (y2 - y1) * (xmin - x1) / (x2 - x1);
        x = xmin;
      }

      if (codeOut == code1)
      {
        x1 = x;
        y1 = y;
        code1 = ComputeCode(x1, y1);
      }
      else
      {
        x2 = x;
        y2 = y;
        code2 = ComputeCode(x2, y2);
      }
    }
  }

  private sealed class ClipScope : IDisposable
  {
    private readonly XGraphics _gfx;
    private readonly XGraphicsState _state;

    public ClipScope(XGraphics gfx, double x, double y, double w, double h)
    {
      _gfx = gfx;
      _state = gfx.Save();
      gfx.IntersectClip(new XRect(x, y, w, h));
    }

    public void Dispose() => _gfx.Restore(_state);
  }

  private static double Mm(double mm) => mm * 72.0 / 25.4;

  private static double MiterInset(double tubeHeight, double miterDeg)
  {
    if (miterDeg <= 0.1)
      return 0;

    var radians = miterDeg * Math.PI / 180.0;
    return Math.Min(tubeHeight * 0.42, tubeHeight * Math.Tan(radians) * 0.38);
  }

  private static string ShortMiter(double miterEnd1Deg, double miterEnd2Deg)
  {
    if (miterEnd1Deg <= 0.1 && miterEnd2Deg <= 0.1)
      return "lotrecht";
    if (miterEnd1Deg > 0.1 && miterEnd2Deg <= 0.1)
      return $"A {miterEnd1Deg:0}°";
    if (miterEnd2Deg > 0.1 && miterEnd1Deg <= 0.1)
      return $"B {miterEnd2Deg:0}°";
    return $"A {miterEnd1Deg:0}° / B {miterEnd2Deg:0}°";
  }

  private static string FormatMm(double mm) =>
    mm.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")) + " mm";

  private static string Trim(string value, int max) =>
    value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

  internal static string BuildManualDrawingNumber(string? orderReference, int sequence)
  {
    var order = SanitizeOrderReference(orderReference);
    return $"{order}_{sequence:000}";
  }

  internal static string SanitizeOrderReference(string? orderReference)
  {
    var trimmed = (orderReference ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
      return "Auftrag";

    var invalid = Path.GetInvalidFileNameChars();
    var chars = trimmed.Select(ch =>
    {
      if (invalid.Contains(ch) || ch is ' ' or '/' or '\\' or ':')
        return '_';
      return ch;
    }).ToArray();

    var result = new string(chars).Trim('_');
    while (result.Contains("__", StringComparison.Ordinal))
      result = result.Replace("__", "_", StringComparison.Ordinal);

    return string.IsNullOrWhiteSpace(result) ? "Auftrag" : result;
  }

  private sealed class ProfileDimensions
  {
    public double OuterWidth { get; init; }
    public double OuterHeight { get; init; }
    public double WallMm { get; init; }

    public static ProfileDimensions Parse(PipeProfileDefinition? profile)
    {
      if (profile is null)
        return new ProfileDimensions { OuterWidth = 50, OuterHeight = 50, WallMm = 3 };

      var numbers = Regex.Matches(profile.Dimensions, @"\d+(?:[.,]\d+)?")
        .Select(match => double.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture))
        .ToList();

      return profile.Kind switch
      {
        PipeProfileKind.Round => new ProfileDimensions
        {
          OuterWidth = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 50,
          OuterHeight = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 50,
          WallMm = numbers.ElementAtOrDefault(1) is > 0 ? numbers[1] : 3
        },
        PipeProfileKind.RoundBar => new ProfileDimensions
        {
          OuterWidth = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 10,
          OuterHeight = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 10,
          WallMm = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] / 2.0 : 5
        },
        PipeProfileKind.Rectangular or PipeProfileKind.CProfile or PipeProfileKind.UProfile or PipeProfileKind.TProfile => new ProfileDimensions
        {
          OuterWidth = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 50,
          OuterHeight = numbers.ElementAtOrDefault(1) is > 0 ? numbers[1] : 50,
          WallMm = numbers.ElementAtOrDefault(2) is > 0 ? numbers[2] : 3
        },
        _ => new ProfileDimensions
        {
          OuterWidth = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 50,
          OuterHeight = numbers.ElementAtOrDefault(0) is > 0 ? numbers[0] : 50,
          WallMm = numbers.ElementAtOrDefault(1) is > 0 ? numbers[1] : 3
        }
      };
    }
  }
}
