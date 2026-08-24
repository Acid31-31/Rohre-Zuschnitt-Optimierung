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
    @"(?<side>links|rechts|left|right)\s*[:=]?\s*(?<value>\d+(?:[.,]\d+)?)\s*(?:°|º|deg|Grad)?",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex RoundProfileRegex = new(
    @"(?:Ø|ø|D(?:urchmesser)?|Rund(?:rohr)?|Rohr)\s*[:=]?\s*(?<d>\d+(?:[.,]\d+)?)\s*(?:mm)?\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SquareProfileRegex = new(
    @"(?:Vierkant(?:rohr)?|Quadrat(?:rohr)?|QR)\s*[:=]?\s*(?<a>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<b>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex RectProfileRegex = new(
    @"(?:Rechteck(?:rohr)?|RH|Flach(?:oval)?)\s*[:=]?\s*(?<a>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<b>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex GenericTripleRegex = new(
    @"(?<a>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<b>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex GenericDoubleRegex = new(
    @"(?<a>\d+(?:[.,]\d+)?)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex PipePartNameRegex = new(
    @"(?:^|[^A-ZÄÖÜ])(?:TUBE|PIPE|ROHR(?:PROFIL)?|QUADRATROHR|VIERKANTROHR|RUNDROHR|RECHTECKROHR|PROFILROHR|ROUND\s+TUBE|SQUARE\s+TUBE|RECT(?:ANGULAR)?\s+TUBE|HOLLOW\s+SECTION|RHS|SHS|CHS)(?:[^A-ZÄÖÜ]|$)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex SheetPartNameRegex = new(
    @"(?:^|[^A-ZÄÖÜ])(?:PLATE|BLECH|COVER|SHEET|PLATTE|LASCHE|GUARD|PANEL|BRACKET|HOLDER|FLANSCH|ABDECKUNG|HALTER|SEPARATION|LOTO|WASHER|SPACER)(?:[^A-ZÄÖÜ]|$)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  public static PdfDrawingAnalysisResult Analyze(string pdfPath)
  {
    var (text, pageWords) = ExtractTextAndDimensionWords(pdfPath);
    if (string.IsNullOrWhiteSpace(text) && pageWords.Count == 0)
    {
      return new PdfDrawingAnalysisResult
      {
        Summary = "Kein lesbarer Text in der PDF gefunden – bitte manuell eingeben."
      };
    }

    var normalized = Normalize(text);
    var fileName = Path.GetFileNameWithoutExtension(pdfPath);
    var partName = ExtractTitleBlockPartName(pageWords);
    var kind = ClassifyPartKind(partName);

    double? lengthMm = null;
    PipeProfileDefinition? profile = null;
    double? leftDeg = null;
    double? rightDeg = null;
    var angleNote = string.Empty;
    var lengthSource = AnalysisValueSource.None;
    var miterSource = AnalysisValueSource.None;

    if (kind == DrawingPartKind.Pipe)
    {
      var searchText = normalized + " " + string.Join(" ", pageWords);
      profile = TryFindProfile(searchText, fileName, allowGenericMatch: true);
      lengthMm = TryFindLengthMm(searchText, pdfPath, pageWords, profile, out lengthSource);
      (leftDeg, rightDeg, angleNote) = TryFindMiterAngles(searchText);
      if (leftDeg is > 0.1 || rightDeg is > 0.1)
        miterSource = AnalysisValueSource.Rules;
    }

    var material = TryFindMaterial(normalized + " " + fileName + " " + partName);

    var summaryParts = new List<string>();
    if (!string.IsNullOrWhiteSpace(partName))
      summaryParts.Add(kind == DrawingPartKind.Pipe ? $"Rohr: {partName}" : $"Kein Rohr: {partName}");
    else if (kind == DrawingPartKind.SheetMetal)
      summaryParts.Add("Kein Rohr (Blech/Abdeckung)");
    else if (kind != DrawingPartKind.Pipe)
      summaryParts.Add("Kein Rohr erkannt");

    if (kind == DrawingPartKind.Pipe)
    {
      if (lengthMm is > 0)
        summaryParts.Add($"Rohrlänge erkannt: {lengthMm:0.##} mm");
      else
        summaryParts.Add("Rohrlänge nicht erkannt");

      if (profile is not null)
        summaryParts.Add($"Profil erkannt: {profile.FullLabel}");
      else
        summaryParts.Add("Profil nicht erkannt");
    }

    if (!string.IsNullOrWhiteSpace(material))
      summaryParts.Add($"Material: {material}");

    var miterEnd1 = MiterNotation.NormalizeInputAngle(leftDeg ?? 0);
    var miterEnd2 = MiterNotation.NormalizeInputAngle(rightDeg ?? 0);

    if (kind == DrawingPartKind.Pipe)
    {
      if (MiterNotation.HasBothMiters(miterEnd1, miterEnd2) || miterEnd1 > 0 || miterEnd2 > 0)
        summaryParts.Add($"Gehrung erkannt: {MiterNotation.Format(miterEnd1, miterEnd2)}");
      else
        summaryParts.Add("Gehrung nicht erkannt");

      if (!string.IsNullOrWhiteSpace(angleNote))
        summaryParts.Add(angleNote);
    }

    return new PdfDrawingAnalysisResult
    {
      LengthMm = lengthMm,
      MiterEnd1Deg = miterEnd1,
      MiterEnd2Deg = miterEnd2,
      Profile = profile,
      Material = material,
      PartName = partName,
      Kind = kind,
      Summary = string.Join(" · ", summaryParts),
      LengthSource = lengthMm is > 0 ? lengthSource : AnalysisValueSource.None,
      MiterSource = (miterEnd1 > 0.1 || miterEnd2 > 0.1) ? miterSource : AnalysisValueSource.None
    };
  }

  private static string ExtractTitleBlockPartName(IReadOnlyList<string> words)
  {
    var paperSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "A0", "A1", "A2", "A3", "A4", "A5"
    };

    for (var i = 0; i < words.Count - 1; i++)
    {
      if (!words[i].Equals("FORMAT", StringComparison.OrdinalIgnoreCase))
        continue;

      var j = i + 1;
      if (j < words.Count && paperSizes.Contains(words[j]))
        j++;

      var parts = new List<string>();
      for (; j < words.Count; j++)
      {
        var word = words[j].Trim();
        if (word.StartsWith("TSL-", StringComparison.OrdinalIgnoreCase)
            || word.Equals("MASSSTAB", StringComparison.OrdinalIgnoreCase)
            || word.Equals("REV.", StringComparison.OrdinalIgnoreCase)
            || word.Equals("SPARES", StringComparison.OrdinalIgnoreCase))
          break;

        if (word.Length == 0 || Regex.IsMatch(word, @"^\d+([.,]\d+)?$"))
          continue;

        parts.Add(word);
        if (parts.Count >= 6)
          break;
      }

      if (parts.Count > 0)
        return string.Join(" ", parts);
    }

    return string.Empty;
  }

  private static DrawingPartKind ClassifyPartKind(string partName)
  {
    if (string.IsNullOrWhiteSpace(partName))
      return DrawingPartKind.Unknown;

    if (PipePartNameRegex.IsMatch(partName))
      return DrawingPartKind.Pipe;

    if (SheetPartNameRegex.IsMatch(partName))
      return DrawingPartKind.SheetMetal;

    return DrawingPartKind.Unknown;
  }

  private static (string Text, IReadOnlyList<string> DimensionWords) ExtractTextAndDimensionWords(string pdfPath)
  {
    var builder = new StringBuilder();
    var words = new List<string>();

    using var document = PdfDocument.Open(pdfPath);
    foreach (var page in document.GetPages())
    {
      builder.AppendLine(page.Text);
      foreach (var word in page.GetWords())
      {
        var value = word.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
          words.Add(value);
      }
    }

    return (builder.ToString(), words);
  }

  private static string Normalize(string text) =>
    text.Replace('\r', ' ')
      .Replace('\n', ' ')
      .Replace('\t', ' ')
      .Replace("mm²", " ")
      .Replace("mm2", " ");

  private static PipeProfileDefinition? TryFindProfile(string text, string fileName, bool allowGenericMatch)
  {
    foreach (Match match in RoundProfileRegex.Matches(text))
    {
      var profile = PipeStockCatalog.TryMatch(
        PipeProfileKind.Round,
        ParseNumber(match.Groups["d"].Value),
        null,
        ParseNumber(match.Groups["t"].Value));
      if (profile is not null)
        return profile;
    }

    foreach (Match match in SquareProfileRegex.Matches(text))
    {
      var profile = PipeStockCatalog.TryMatch(
        PipeProfileKind.Square,
        ParseNumber(match.Groups["a"].Value),
        ParseNumber(match.Groups["b"].Value),
        ParseNumber(match.Groups["t"].Value));
      if (profile is not null)
        return profile;
    }

    foreach (Match match in RectProfileRegex.Matches(text))
    {
      var profile = PipeStockCatalog.TryMatch(
        PipeProfileKind.Rectangular,
        ParseNumber(match.Groups["a"].Value),
        ParseNumber(match.Groups["b"].Value),
        ParseNumber(match.Groups["t"].Value));
      if (profile is not null)
        return profile;
    }

    if (!allowGenericMatch)
      return null;

    foreach (Match match in GenericTripleRegex.Matches(text + " " + fileName))
    {
      var a = ParseNumber(match.Groups["a"].Value);
      var b = ParseNumber(match.Groups["b"].Value);
      var t = ParseNumber(match.Groups["t"].Value);
      if (!IsPlausibleSection(a) || !IsPlausibleSection(b) || !IsPlausibleThickness(t))
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
    }

    foreach (Match match in GenericDoubleRegex.Matches(text + " " + fileName))
    {
      var d = ParseNumber(match.Groups["a"].Value);
      var t = ParseNumber(match.Groups["t"].Value);
      if (!IsPlausibleSection(d) || !IsPlausibleThickness(t))
        continue;

      var round = PipeStockCatalog.TryMatch(PipeProfileKind.Round, d, null, t);
      if (round is not null)
        return round;
    }

    return null;
  }

  private static string? TryFindMaterial(string text)
  {
    if (Regex.IsMatch(text, @"\b(edelstahl|va[\s\-]?\d+|v2a|v4a|1\.4301|1\.4404|stainless|inox)\b", RegexOptions.IgnoreCase))
      return PipeMaterialTypes.Stainless;

    if (Regex.IsMatch(text, @"\b(aluminium|aluminum|alu|almg\d*)\b", RegexOptions.IgnoreCase))
      return PipeMaterialTypes.Aluminum;

    if (Regex.IsMatch(text, @"\b(stahl|s235|s355|baustahl|black\s*steel|1\.0038)\b", RegexOptions.IgnoreCase))
      return PipeMaterialTypes.Steel;

    return null;
  }

  private static double? TryFindLengthMm(
    string text,
    string pdfPath,
    IReadOnlyList<string> dimensionWords,
    PipeProfileDefinition? profile,
    out AnalysisValueSource source)
  {
    source = AnalysisValueSource.None;
    foreach (Match match in LengthWithUnitRegex.Matches(text))
    {
      var value = ParseNumber(match.Groups["value"].Value);
      var unit = match.Groups["unit"].Value.ToLowerInvariant();
      var mm = ConvertToMm(value, unit);
      if (IsPlausiblePipeLength(mm))
      {
        source = AnalysisValueSource.Rules;
        return mm;
      }
    }

    var fromCad = CompanionCadLengthService.TryGetLengthMm(pdfPath, profile);
    if (fromCad is > 0)
    {
      source = AnalysisValueSource.Step;
      return fromCad;
    }

    var profileDims = CollectProfileDimensionValues(text + " " + Path.GetFileNameWithoutExtension(pdfPath));

    var mmValues = PlainMmRegex.Matches(text)
      .Select(match => ParseNumber(match.Groups["value"].Value))
      .Where(IsPlausiblePipeLength)
      .Where(value => !profileDims.Contains(value))
      .Where(value => value >= 100)
      .ToList();

    if (mmValues.Count > 0)
    {
      source = AnalysisValueSource.Rules;
      return mmValues.Max();
    }

    // Tesla-/ISO-Zeichnungen: Maße oft ohne "mm" (nur Zahl wie 1199,6).
    var fromWords = TryFindLengthFromDimensionWords(dimensionWords, profileDims);
    if (fromWords is > 0)
    {
      source = AnalysisValueSource.Rules;
      return fromWords;
    }

    var fromName = TryParseLengthFromFileName(Path.GetFileNameWithoutExtension(pdfPath));
    if (fromName is > 0)
      source = AnalysisValueSource.Rules;
    return fromName;
  }

  private static readonly HashSet<double> TitleBlockNoiseLengths =
  [
    30, 60, 85.8, 100, 120, 200, 300, 400, 500, 1000, 1015, 2000, 4000, 2768
  ];

  private static readonly Regex BareDimensionWordRegex = new(
    @"^\d{2,5}(?:[.,]\d{1,3})?$",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static double? TryFindLengthFromDimensionWords(
    IReadOnlyList<string> dimensionWords,
    HashSet<double> profileDims)
  {
    double bestScore = double.MinValue;
    double? best = null;

    foreach (var rawWord in dimensionWords)
    {
      var raw = rawWord.Trim();
      if (!BareDimensionWordRegex.IsMatch(raw))
        continue;

      var decimalPoint = raw.IndexOfAny([',', '.']);
      var decimals = decimalPoint < 0 ? 0 : raw.Length - decimalPoint - 1;
      if (decimals >= 3)
        continue;

      if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        continue;

      if (!IsPlausiblePipeLength(value) || value < 80 || value > 6000)
        continue;

      if (profileDims.Contains(value) || IsTitleBlockNoiseLength(value))
        continue;

      var score = ScoreDimensionCandidate(value, raw);
      if (score <= bestScore)
        continue;

      bestScore = score;
      best = value;
    }

    return best;
  }

  private static bool IsTitleBlockNoiseLength(double value) =>
    TitleBlockNoiseLengths.Any(noise => Math.Abs(noise - value) < 0.001);

  private static double ScoreDimensionCandidate(double value, string raw)
  {
    var decimalPoint = raw.IndexOfAny([',', '.']);
    var decimals = decimalPoint < 0 ? 0 : raw.Length - decimalPoint - 1;

    // Werkstattmaße haben oft 1 Nachkommastelle; viele Nachkommastellen wirken wie CAD-Koordinaten.
    var score = value;
    score += decimals switch
    {
      1 => 5000,
      2 => 2000,
      >= 3 => -3000,
      _ => 0
    };

    if (value is >= 150 and <= 5500)
      score += 500;

    return score;
  }

  private static HashSet<double> CollectProfileDimensionValues(string text)
  {
    var values = new HashSet<double>();
    foreach (Match match in GenericTripleRegex.Matches(text))
    {
      values.Add(ParseNumber(match.Groups["a"].Value));
      values.Add(ParseNumber(match.Groups["b"].Value));
      values.Add(ParseNumber(match.Groups["t"].Value));
    }

    foreach (Match match in RoundProfileRegex.Matches(text))
    {
      values.Add(ParseNumber(match.Groups["d"].Value));
      values.Add(ParseNumber(match.Groups["t"].Value));
    }

    return values;
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

    var note = left is null && right is null
      ? "Hinweis: Gehrung bitte manuell prüfen."
      : string.Empty;

    return (left, right, note);
  }

  private static double? TryParseLengthFromFileName(string fileName)
  {
    // Nur eindeutige Längenangaben, nie Zeichnungsnummern wie TSL-11480-383
    var withUnit = Regex.Match(
      fileName,
      @"(?:^|[_\-\s])(?:L|Laenge|Länge|Len)?\s*(?<value>\d{2,5}(?:[.,]\d+)?)\s*mm(?:$|[_\-\s])",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    if (withUnit.Success)
    {
      var value = ParseNumber(withUnit.Groups["value"].Value);
      return IsPlausiblePipeLength(value) ? value : null;
    }

    return null;
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

  private static bool IsPlausibleSection(double mm) => mm is >= 8 and <= 250;

  private static bool IsPlausibleThickness(double mm) => mm is >= 0.5 and <= 20;

  private static bool IsPlausibleMiterAngle(double deg) => deg is >= 0 and <= 90;

  private static bool IsLeftSide(string side) =>
    side is "links" or "left" or "l" or "l.";

  private static bool IsRightSide(string side) =>
    side is "rechts" or "right" or "r" or "r.";
}
