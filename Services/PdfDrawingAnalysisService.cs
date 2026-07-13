using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PdfDrawingAnalysisService
{
  private static readonly Regex LengthWithUnitRegex = new(
    @"(?:L(?:änge|G|änge)?|Rohrlänge|Laenge|Length)\s*[=:]\s*(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>mm|m|cm)?",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex PlainMmRegex = new(
    @"(?<value>\d+(?:[.,]\d+)?)\s*mm\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex AngleRegex = new(
    @"(?<value>\d+(?:[.,]\d+)?)\s*(?:°|º|deg|Grad)\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SideAngleRegex = new(
    @"(?<side>links|rechts|left|right|l\.?|r\.?)\s*[:=]?\s*(?<value>\d+(?:[.,]\d+)?)\s*(?:°|º|deg|Grad)?",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  public static PdfDrawingAnalysisResult Analyze(string pdfPath)
  {
    var text = ExtractText(pdfPath);
    if (string.IsNullOrWhiteSpace(text))
    {
      return new PdfDrawingAnalysisResult
      {
        Summary = "Kein lesbarer Text in der PDF gefunden – bitte manuell eingeben."
      };
    }

    var normalized = Normalize(text);
    var lengthMm = TryFindLengthMm(normalized, pdfPath);
    var (leftDeg, rightDeg, angleNote) = TryFindMiterAngles(normalized);

    var summaryParts = new List<string>();
    if (lengthMm is > 0)
      summaryParts.Add($"Rohrlänge erkannt: {lengthMm:0.##} mm");
    else
      summaryParts.Add("Rohrlänge nicht erkannt");

    var miterEnd1 = MiterNotation.NormalizeInputAngle(leftDeg ?? 0);
    var miterEnd2 = MiterNotation.NormalizeInputAngle(rightDeg ?? 0);

    if (MiterNotation.HasBothMiters(miterEnd1, miterEnd2))
      summaryParts.Add($"Gehrung erkannt: {MiterNotation.Format(miterEnd1, miterEnd2)}");
    else if (miterEnd1 > 0 || miterEnd2 > 0)
      summaryParts.Add($"Gehrung erkannt: {MiterNotation.Format(miterEnd1, miterEnd2)}");
    else
      summaryParts.Add("Gehrung nicht erkannt");

    if (!string.IsNullOrWhiteSpace(angleNote))
      summaryParts.Add(angleNote);

    return new PdfDrawingAnalysisResult
    {
      LengthMm = lengthMm,
      MiterEnd1Deg = miterEnd1,
      MiterEnd2Deg = miterEnd2,
      Summary = string.Join(" · ", summaryParts)
    };
  }

  private static string ExtractText(string pdfPath)
  {
    var builder = new StringBuilder();

    using var document = PdfDocument.Open(pdfPath);
    foreach (var page in document.GetPages())
    {
      builder.AppendLine(page.Text);
    }

    return builder.ToString();
  }

  private static string Normalize(string text) =>
    text.Replace('\r', ' ')
      .Replace('\n', ' ')
      .Replace('\t', ' ')
      .Replace("mm²", " ")
      .Replace("mm2", " ");

  private static double? TryFindLengthMm(string text, string pdfPath)
  {
    foreach (Match match in LengthWithUnitRegex.Matches(text))
    {
      var value = ParseNumber(match.Groups["value"].Value);
      var unit = match.Groups["unit"].Value.ToLowerInvariant();
      var mm = ConvertToMm(value, unit);
      if (IsPlausiblePipeLength(mm))
        return mm;
    }

    var mmValues = PlainMmRegex.Matches(text)
      .Select(match => ParseNumber(match.Groups["value"].Value))
      .Where(IsPlausiblePipeLength)
      .ToList();

    if (mmValues.Count > 0)
      return mmValues.Max();

    var fromFileName = TryParseLengthFromFileName(Path.GetFileNameWithoutExtension(pdfPath));
    return fromFileName;
  }

  private static (double? Left, double? Right, string Note) TryFindMiterAngles(string text)
  {
    double? left = null;
    double? right = null;

    foreach (Match match in SideAngleRegex.Matches(text))
    {
      var side = match.Groups["side"].Value.ToLowerInvariant();
      var value = ParseNumber(match.Groups["value"].Value);
      if (!IsPlausibleMiterAngle(value))
        continue;

      if (IsLeftSide(side))
        left = value;
      else if (IsRightSide(side))
        right = value;
    }

    if (text.Contains("gehrung", StringComparison.OrdinalIgnoreCase)
        || text.Contains("schnittgehrung", StringComparison.OrdinalIgnoreCase))
    {
      var angles = AngleRegex.Matches(text)
        .Select(match => ParseNumber(match.Groups["value"].Value))
        .Where(IsPlausibleMiterAngle)
        .Distinct()
        .ToList();

      if (angles.Count == 1)
      {
        left ??= angles[0];
        right ??= angles[0];
      }
      else if (angles.Count >= 2)
      {
        left ??= angles[0];
        right ??= angles[1];
      }
    }
    else
    {
      var genericAngles = AngleRegex.Matches(text)
        .Select(match => ParseNumber(match.Groups["value"].Value))
        .Where(IsPlausibleMiterAngle)
        .Distinct()
        .Take(2)
        .ToList();

      if (left is null && genericAngles.Count > 0)
        left = genericAngles[0];
      if (right is null && genericAngles.Count > 1)
        right = genericAngles[1];
      else if (right is null && genericAngles.Count == 1)
        right = genericAngles[0];
    }

    var note = left is null && right is null
      ? "Hinweis: Gehrung bitte manuell prüfen."
      : string.Empty;

    return (left, right, note);
  }

  private static double? TryParseLengthFromFileName(string fileName)
  {
    var match = Regex.Match(fileName, @"(?<value>\d{3,5})(?:mm)?", RegexOptions.CultureInvariant);
    if (!match.Success)
      return null;

    var value = ParseNumber(match.Groups["value"].Value);
    return IsPlausiblePipeLength(value) ? value : null;
  }

  private static double ConvertToMm(double value, string unit) =>
    unit switch
    {
      "m" => value * 1000,
      "cm" => value * 10,
      _ => value
    };

  private static double ParseNumber(string raw) =>
    double.Parse(raw.Replace(',', '.'), CultureInfo.InvariantCulture);

  private static bool IsPlausiblePipeLength(double mm) => mm is >= 20 and <= 12000;

  private static bool IsPlausibleMiterAngle(double deg) => deg is >= 0 and <= 90;

  private static bool IsLeftSide(string side) =>
    side is "links" or "left" or "l" or "l.";

  private static bool IsRightSide(string side) =>
    side is "rechts" or "right" or "r" or "r.";
}
