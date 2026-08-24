using System.Globalization;
using System.Text.RegularExpressions;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class PipePartParser
{
  private static readonly Regex TubeKeywordRegex = new(
    @"\b(?:TUBE|PIPE|ROHR(?:PROFIL)?|QUADRATROHR|VIERKANTROHR|RUNDROHR|RECHTECKROHR|PROFILROHR|RHS|SHS|CHS)\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SheetKeywordRegex = new(
    @"\b(?:PLATE|BLECH|COVER|SHEET|PLATTE|LASCHE|GUARD|PANEL|BRACKET|HOLDER|FLANSCH|ABDECKUNG|NUT|WASHER|SPACER)\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex ProfileRegex = new(
    @"(?<a>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<b>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex LengthLabelRegex = new(
    @"(?:L(?:änge|aenge|ength)?)\s*[=:]\s*(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>mm|cm|m)?",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex LengthMmRegex = new(
    @"(?<value>\d{2,5}(?:[.,]\d+)?)\s*mm\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex BareNumberRegex = new(
    @"\d{2,5}(?:[.,]\d+)?",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex DrawingNumberRegex = new(
    @"\b[A-Z]{1,5}-\d{3,}(?:-\d+)?\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  public static bool IsPipeDescription(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return false;

    if (TubeKeywordRegex.IsMatch(text))
      return true;

    return ProfileRegex.IsMatch(text) && !SheetKeywordRegex.IsMatch(text);
  }

  public static PipeProfileDefinition? TryParseProfile(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return null;

    foreach (Match match in ProfileRegex.Matches(text))
    {
      var a = ParseNumber(match.Groups["a"].Value);
      var b = ParseNumber(match.Groups["b"].Value);
      var t = ParseNumber(match.Groups["t"].Value);
      if (t is < 0.4 or > 20)
        continue;

      if (Math.Abs(a - b) < 0.15)
      {
        var square = PipeStockCatalog.TryMatch(PipeProfileKind.Square, a, b, t);
        if (square is not null)
          return square;
      }

      var rect = PipeStockCatalog.TryMatch(PipeProfileKind.Rectangular, a, b, t);
      if (rect is not null)
        return rect;

      var round = PipeStockCatalog.TryMatch(PipeProfileKind.Round, a, null, t);
      if (round is not null && Math.Abs(a - b) < 0.15)
        return round;
    }

    return null;
  }

  public static double? TryParseLengthMm(string? text, PipeProfileDefinition? profile)
  {
    if (string.IsNullOrWhiteSpace(text))
      return null;

    var cleaned = DrawingNumberRegex.Replace(text, " ");

    foreach (Match match in LengthLabelRegex.Matches(cleaned))
    {
      var mm = ToMm(ParseNumber(match.Groups["value"].Value), match.Groups["unit"].Value);
      if (IsPlausibleLength(mm, profile))
        return mm;
    }

    foreach (Match match in LengthMmRegex.Matches(cleaned))
    {
      var mm = ParseNumber(match.Groups["value"].Value);
      if (IsPlausibleLength(mm, profile))
        return mm;
    }

    double? unique = null;
    foreach (Match match in BareNumberRegex.Matches(cleaned))
    {
      var mm = ParseNumber(match.Value);
      if (!IsPlausibleLength(mm, profile))
        continue;

      if (unique is not null && Math.Abs(unique.Value - mm) > 0.2)
        return null;

      unique = mm;
    }

    return unique;
  }

  public static bool IsPlausibleLength(double mm, PipeProfileDefinition? profile)
  {
    if (mm is < 80 or > 12000)
      return false;

    if (profile is not null
        && PipeStockCatalog.TryParseCatalogSize(profile, out var primary, out var secondary, out _))
    {
      if (Math.Abs(mm - primary) < 0.2 || Math.Abs(mm - secondary) < 0.2)
        return false;
    }

    return true;
  }

  private static double ToMm(double value, string unit) =>
    unit.ToLowerInvariant() switch
    {
      "m" => value * 1000,
      "cm" => value * 10,
      _ => value
    };

  private static double ParseNumber(string raw) =>
    double.Parse(raw.Replace(',', '.'), CultureInfo.InvariantCulture);
}
