using System.Text;
using System.Text.RegularExpressions;

namespace RohreZuschnittOptimierung.Services;

internal sealed class ReleaseChangeGroup
{
  public string RevisionLabel { get; init; } = string.Empty;
  public int Revision { get; init; }
  public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
}

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
    return ExtractChangeGroups(releaseNotes)
      .SelectMany(group => group.Items)
      .ToList();
  }

  public static IReadOnlyList<ReleaseChangeGroup> ExtractChangeGroups(string releaseNotes)
  {
    releaseNotes = NormalizeReleaseNotes(releaseNotes);
    if (string.IsNullOrWhiteSpace(releaseNotes))
      return Array.Empty<ReleaseChangeGroup>();

    var groups = new List<ReleaseChangeGroup>();
    var currentLabel = string.Empty;
    var currentRevision = 0;
    var currentItems = new List<string>();
    var inChangesSection = false;

    void Flush()
    {
      if (currentItems.Count == 0)
      {
        currentLabel = string.Empty;
        currentRevision = 0;
        return;
      }

      groups.Add(new ReleaseChangeGroup
      {
        RevisionLabel = string.IsNullOrWhiteSpace(currentLabel) ? string.Empty : currentLabel,
        Revision = currentRevision,
        Items = currentItems.ToList()
      });
      currentLabel = string.Empty;
      currentRevision = 0;
      currentItems = new List<string>();
    }

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

      if (TryParseRevisionHeading(line, out var headingLabel, out var headingRevision))
      {
        Flush();
        currentLabel = headingLabel;
        currentRevision = headingRevision;
        inChangesSection = true;
        continue;
      }

      if (inChangesSection && IsSectionHeading(line))
      {
        Flush();
        break;
      }

      if (line.StartsWith("- ", StringComparison.Ordinal))
      {
        var item = line[2..].Trim();
        if (TryParsePrefixedItem(item, out var itemLabel, out var itemRevision, out var itemText))
        {
          if (!string.Equals(currentLabel, itemLabel, StringComparison.OrdinalIgnoreCase)
              && currentItems.Count > 0)
            Flush();

          currentLabel = itemLabel;
          currentRevision = itemRevision;
          currentItems.Add(itemText);
        }
        else
        {
          currentItems.Add(item);
        }

        inChangesSection = true;
        continue;
      }

      if (inChangesSection && string.IsNullOrWhiteSpace(line))
        continue;
    }

    Flush();
    return groups;
  }

  public static IReadOnlyList<ReleaseChangeGroup> ParseChangelog(string markdown)
  {
    markdown = NormalizeReleaseNotes(markdown);
    if (string.IsNullOrWhiteSpace(markdown))
      return Array.Empty<ReleaseChangeGroup>();

    var groups = new List<ReleaseChangeGroup>();
    string? currentLabel = null;
    var currentRevision = 0;
    var currentItems = new List<string>();

    void Flush()
    {
      if (currentLabel is null || currentItems.Count == 0)
      {
        currentLabel = null;
        currentItems = new List<string>();
        return;
      }

      groups.Add(new ReleaseChangeGroup
      {
        RevisionLabel = currentLabel,
        Revision = currentRevision,
        Items = currentItems.ToList()
      });
      currentLabel = null;
      currentRevision = 0;
      currentItems = new List<string>();
    }

    foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
    {
      var line = rawLine.Trim();
      if (TryParseRevisionHeading(line, out var label, out var revision))
      {
        Flush();
        currentLabel = label;
        currentRevision = revision;
        continue;
      }

      if (currentLabel is not null && line.StartsWith("- ", StringComparison.Ordinal))
        currentItems.Add(line[2..].Trim());
    }

    Flush();
    return groups;
  }

  public static string ComposeNotesSince(
    int installedRevision,
    IReadOnlyList<ReleaseChangeGroup> changelogGroups,
    string fallbackLatestNotes)
  {
    var newer = changelogGroups
      .Where(group => group.Revision > installedRevision && group.Items.Count > 0)
      .OrderByDescending(group => group.Revision)
      .ToList();

    if (newer.Count == 0)
    {
      newer = ExtractChangeGroups(fallbackLatestNotes)
        .Where(group => group.Revision <= 0 || group.Revision > installedRevision)
        .Where(group => group.Items.Count > 0)
        .ToList();
    }

    if (newer.Count == 0)
      return NormalizeReleaseNotes(fallbackLatestNotes);

    var builder = new StringBuilder();
    builder.AppendLine("Änderungen:");
    foreach (var group in newer)
    {
      builder.AppendLine();
      if (!string.IsNullOrWhiteSpace(group.RevisionLabel) && newer.Count > 1)
        builder.AppendLine(group.RevisionLabel);
      foreach (var item in group.Items)
        builder.AppendLine("- " + item);
    }

    return builder.ToString().TrimEnd();
  }

  private static bool TryParseRevisionHeading(string line, out string label, out int revision)
  {
    label = string.Empty;
    revision = 0;
    var match = Regex.Match(line, @"^(?:#+\s*)?(R(\d+))\s*:?\s*$", RegexOptions.IgnoreCase);
    if (!match.Success)
      return false;

    label = "R" + match.Groups[2].Value;
    revision = int.Parse(match.Groups[2].Value);
    return true;
  }

  private static bool TryParsePrefixedItem(
    string item,
    out string label,
    out int revision,
    out string text)
  {
    label = string.Empty;
    revision = 0;
    text = item;
    var match = Regex.Match(item, @"^(?:\()?R(\d+)(?:\))?\s*:\s*(.+)$", RegexOptions.IgnoreCase);
    if (!match.Success)
      return false;

    revision = int.Parse(match.Groups[1].Value);
    label = "R" + match.Groups[1].Value;
    text = match.Groups[2].Value.Trim();
    return text.Length > 0;
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
    if (Regex.IsMatch(line, @"^Änderungen\s*(in diesem Update|seit der installierten Version)?\s*:?\s*$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(line, @"^Changes\s*:?\s*$", RegexOptions.IgnoreCase))
      return true;

    return line.Contains("nderungen", StringComparison.OrdinalIgnoreCase)
           && line.TrimEnd().EndsWith(":", StringComparison.Ordinal)
           && !TryParseRevisionHeading(line, out _, out _);
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
    && line.Length < 80
    && !TryParseRevisionHeading(line, out _, out _);

  private static void TrimEmptyEdges(List<string> lines)
  {
    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
      lines.RemoveAt(0);

    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
      lines.RemoveAt(lines.Count - 1);
  }
}
