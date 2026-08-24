using System.Text;
using System.Text.RegularExpressions;

namespace RohreZuschnittOptimierung.Services;

internal static class ReleaseNotesFormatter
{
  public static string NormalizeReleaseNotes(string releaseNotes)
  {
    if (string.IsNullOrWhiteSpace(releaseNotes))
      return string.Empty;

    var text = TryFixUtf8Mojibake(releaseNotes.Trim());
    text = text
      .Replace("\\r\\n", "\n", StringComparison.Ordinal)
      .Replace("\\n", "\n", StringComparison.Ordinal)
      .Replace("\\r", "\n", StringComparison.Ordinal);

    return text;
  }

  public static string FormatForDisplay(string releaseNotes)
  {
    releaseNotes = NormalizeReleaseNotes(releaseNotes);
    if (string.IsNullOrWhiteSpace(releaseNotes))
      return "Keine Änderungsbeschreibung verfügbar.";

    var lines = releaseNotes
      .Replace("\r\n", "\n")
      .Split('\n')
      .Select(line => line.TrimEnd())
      .Where(line => !ShouldHideFromUser(line))
      .ToList();

    TrimEmptyEdges(lines);

    if (lines.Count == 0)
      return "Keine Änderungsbeschreibung verfügbar.";

    return string.Join(Environment.NewLine, lines);
  }

  public static IReadOnlyList<string> ExtractChangeItems(string releaseNotes)
  {
    releaseNotes = NormalizeReleaseNotes(releaseNotes);
    if (string.IsNullOrWhiteSpace(releaseNotes))
      return Array.Empty<string>();

    var items = new List<string>();
    var inChangesSection = false;

    foreach (var rawLine in releaseNotes.Replace("\r\n", "\n").Split('\n'))
    {
      var line = rawLine.Trim();
      if (ShouldHideFromUser(line))
        continue;

      if (IsChangesHeading(line))
      {
        inChangesSection = true;
        continue;
      }

      if (inChangesSection && IsSectionHeading(line))
        break;

      if (line.StartsWith("- ", StringComparison.Ordinal))
      {
        items.Add(line[2..].Trim());
        inChangesSection = true;
        continue;
      }

      if (inChangesSection && string.IsNullOrWhiteSpace(line))
        continue;

      if (!inChangesSection && line.StartsWith("- ", StringComparison.Ordinal))
        items.Add(line[2..].Trim());
    }

    return items;
  }

  private static bool ShouldHideFromUser(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
      return false;

    if (Regex.IsMatch(line, @"^SHA-?256\s*[:=]", RegexOptions.IgnoreCase))
      return true;

    if (Regex.IsMatch(line, @"^Rohre Zuschnitt Optimierung\s+v", RegexOptions.IgnoreCase))
      return true;

    return false;
  }

  private static bool IsChangesHeading(string line)
  {
    if (Regex.IsMatch(line, @"^Änderungen\s*(in diesem Update)?\s*:?\s*$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(line, @"^Changes\s*:?\s*$", RegexOptions.IgnoreCase))
      return true;

    return line.Contains("nderungen", StringComparison.OrdinalIgnoreCase)
           && line.TrimEnd().EndsWith(":", StringComparison.Ordinal);
  }

  private static string TryFixUtf8Mojibake(string text)
  {
    if (string.IsNullOrWhiteSpace(text)
        || !text.Contains('Ã', StringComparison.Ordinal))
      return text;

    try
    {
      var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
      var repaired = Encoding.UTF8.GetString(bytes);
      if (repaired.Contains('ä', StringComparison.Ordinal)
          || repaired.Contains('ö', StringComparison.Ordinal)
          || repaired.Contains('ü', StringComparison.Ordinal)
          || repaired.Contains('Ä', StringComparison.Ordinal))
        return repaired;
    }
    catch
    {
      // ignore
    }

    return text;
  }

  private static bool IsSectionHeading(string line) =>
    !line.StartsWith("- ", StringComparison.Ordinal)
    && line.EndsWith(":", StringComparison.Ordinal)
    && line.Length < 80;

  private static void TrimEmptyEdges(List<string> lines)
  {
    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
      lines.RemoveAt(0);

    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
      lines.RemoveAt(lines.Count - 1);
  }
}
