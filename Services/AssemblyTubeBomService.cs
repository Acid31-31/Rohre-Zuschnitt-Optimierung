using System.IO;
using UglyToad.PdfPig;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal sealed class AssemblyTubePosition
{
  public required string SourceFileName { get; init; }
  public string? DrawingNumber { get; init; }
  public string Description { get; init; } = string.Empty;
  public PipeProfileDefinition? Profile { get; init; }
  public double? LengthMm { get; init; }
  public int Quantity { get; init; } = 1;
  public int? Position { get; init; }
}

internal static class AssemblyTubeBomService
{
  public static IReadOnlyList<AssemblyTubePosition> Extract(IEnumerable<string> pdfPaths)
  {
    var result = new List<AssemblyTubePosition>();
    foreach (var path in pdfPaths.Where(File.Exists))
    {
      try
      {
        result.AddRange(ExtractFromPdf(path));
      }
      catch
      {
        // Einzelne unlesbare PDFs überspringen.
      }
    }

    return result;
  }

  private static List<AssemblyTubePosition> ExtractFromPdf(string pdfPath)
  {
    var found = new List<AssemblyTubePosition>();
    using var document = PdfDocument.Open(pdfPath);
    var fileName = Path.GetFileName(pdfPath);

    foreach (var page in document.GetPages())
    {
      var words = page.GetWords()
        .Select(word => new BomWord(
          word.Text.Trim(),
          word.BoundingBox.Left,
          (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2.0))
        .Where(word => word.Text.Length > 0)
        .OrderByDescending(word => word.Y)
        .ThenBy(word => word.X)
        .ToList();

      if (words.Count == 0)
        continue;

      var rows = ClusterRows(words);
      var header = rows
        .Select(ParseHeader)
        .FirstOrDefault(entry => entry is not null && entry.IsBomHeader);

      if (header is not null)
      {
        foreach (var row in rows)
        {
          var position = ParseRow(row, header, fileName);
          if (position is not null)
            found.Add(position);
        }

        continue;
      }

      found.AddRange(ExtractFromTubeKeywords(rows, fileName));
    }

    return found
      .GroupBy(item =>
        string.Join("|", item.SourceFileName, item.Position?.ToString() ?? "", item.DrawingNumber ?? "", item.Description),
        StringComparer.OrdinalIgnoreCase)
      .Select(group => group.First())
      .ToList();
  }

  private static List<List<BomWord>> ClusterRows(IReadOnlyList<BomWord> words)
  {
    var rows = new List<List<BomWord>>();
    foreach (var word in words)
    {
      var row = rows.LastOrDefault();
      if (row is null || Math.Abs(row[0].Y - word.Y) > 3.2)
        rows.Add([word]);
      else
        row.Add(word);
    }

    foreach (var row in rows)
      row.Sort((a, b) => a.X.CompareTo(b.X));

    return rows;
  }

  private static BomHeader? ParseHeader(IReadOnlyList<BomWord> row)
  {
    var columns = new List<BomColumn>();
    foreach (var word in row)
    {
      var kind = ClassifyHeader(word.Text);
      if (kind == BomColumnKind.None)
        continue;

      columns.Add(new BomColumn(kind, word.X));
    }

    if (columns.Count == 0)
      return null;

    var kinds = columns.Select(c => c.Kind).ToHashSet();
    var isBom = (kinds.Contains(BomColumnKind.Description) && kinds.Contains(BomColumnKind.Drawing))
                || (kinds.Contains(BomColumnKind.Pos) && kinds.Contains(BomColumnKind.Qty) && kinds.Contains(BomColumnKind.Description));

    return new BomHeader(columns, isBom);
  }

  private static BomColumnKind ClassifyHeader(string text)
  {
    var n = NormalizeHeader(text);
    if (n is "pos" or "position" or "posnr" or "lfd" or "lfdnr" or "linenumber" or "line")
      return BomColumnKind.Pos;
    if (n is "pcs" or "qty" or "quantity" or "menge" or "stk" or "stck" or "stuck" or "stück" or "anzahl")
      return BomColumnKind.Qty;
    if (n is "cat" or "category" or "kategorie")
      return BomColumnKind.Cat;
    if (n is "description" or "benennung" or "bezeichnung" or "beschr" or "desc")
      return BomColumnKind.Description;
    if (n is "drw" or "drwnumber" or "drawing" or "drawingno" or "drawingnumber"
        or "zeichnung" or "zeichnungsnr" or "zeichnungsnummer")
      return BomColumnKind.Drawing;
    if (n.Contains("zeichnung") || (n.Contains("drawing") && !n.Contains("rev") && !n.Contains("date")))
      return BomColumnKind.Drawing;
    if (n is "length" or "lange" or "laenge" or "rohrlange" or "cutlength" or "schnittlange" or "mass" or "maß")
      return BomColumnKind.Length;
    return BomColumnKind.None;
  }

  private static AssemblyTubePosition? ParseRow(IReadOnlyList<BomWord> row, BomHeader header, string fileName)
  {
    var joined = string.Join(" ", row.Select(w => w.Text));
    if (!PipePartParser.IsPipeDescription(joined))
      return null;

    string Take(BomColumnKind kind) =>
      string.Join(" ", row.Where(word => header.KindAt(word.X) == kind).Select(word => word.Text)).Trim();

    var description = Take(BomColumnKind.Description);
    if (string.IsNullOrWhiteSpace(description))
      description = joined;

    var drawing = Take(BomColumnKind.Drawing);
    if (string.IsNullOrWhiteSpace(drawing))
      drawing = row.Select(w => w.Text).FirstOrDefault(IsDrawingNumber) ?? string.Empty;

    var profile = PipePartParser.TryParseProfile(description)
                  ?? PipePartParser.TryParseProfile(joined);

    var lengthRaw = Take(BomColumnKind.Length);
    var length = PipePartParser.TryParseLengthMm(lengthRaw, profile)
                 ?? PipePartParser.TryParseLengthMm(description, profile);

    int? quantity = null;
    if (int.TryParse(Take(BomColumnKind.Qty), out var qty) && qty > 0 && qty < 100000)
      quantity = qty;

    int? position = null;
    if (int.TryParse(Take(BomColumnKind.Pos), out var pos) && pos > 0 && pos < 10000)
      position = pos;

    return new AssemblyTubePosition
    {
      SourceFileName = fileName,
      DrawingNumber = string.IsNullOrWhiteSpace(drawing) ? null : drawing,
      Description = description,
      Profile = profile,
      LengthMm = length,
      Quantity = quantity ?? 1,
      Position = position
    };
  }

  private static List<AssemblyTubePosition> ExtractFromTubeKeywords(IReadOnlyList<List<BomWord>> rows, string fileName)
  {
    var found = new List<AssemblyTubePosition>();
    foreach (var row in rows)
    {
      var tubeIndex = row.FindIndex(w =>
        w.Text.Equals("TUBE", StringComparison.OrdinalIgnoreCase)
        || w.Text.Equals("ROHR", StringComparison.OrdinalIgnoreCase)
        || w.Text.Equals("PIPE", StringComparison.OrdinalIgnoreCase));
      if (tubeIndex < 0)
        continue;

      var after = row.Skip(tubeIndex).Select(w => w.Text).ToList();
      var joined = string.Join(" ", after);
      if (!PipePartParser.IsPipeDescription(joined))
        continue;

      var profile = PipePartParser.TryParseProfile(joined);
      var drawing = after.FirstOrDefault(IsDrawingNumber);
      var length = PipePartParser.TryParseLengthMm(joined, profile);

      found.Add(new AssemblyTubePosition
      {
        SourceFileName = fileName,
        DrawingNumber = drawing,
        Description = joined,
        Profile = profile,
        LengthMm = length,
        Quantity = 1
      });
    }

    return found;
  }

  private static bool IsDrawingNumber(string word) =>
    word.StartsWith("TSL-", StringComparison.OrdinalIgnoreCase)
    || System.Text.RegularExpressions.Regex.IsMatch(word, @"^[A-Z]{1,5}-\d{3,}(?:-\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

  private static string NormalizeHeader(string value)
  {
    var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
    return new string(chars).Replace("ä", "a").Replace("ö", "o").Replace("ü", "u").Replace("ß", "ss");
  }

  private sealed record BomWord(string Text, double X, double Y);

  private enum BomColumnKind
  {
    None,
    Pos,
    Qty,
    Cat,
    Description,
    Drawing,
    Length
  }

  private sealed record BomColumn(BomColumnKind Kind, double X);

  private sealed record BomHeader(IReadOnlyList<BomColumn> Columns, bool IsBomHeader)
  {
    public BomColumnKind KindAt(double x)
    {
      BomColumn? best = null;
      var bestDist = double.MaxValue;
      foreach (var column in Columns)
      {
        var dist = Math.Abs(column.X - x);
        if (dist < bestDist)
        {
          bestDist = dist;
          best = column;
        }
      }

      return bestDist <= 42 ? (best?.Kind ?? BomColumnKind.None) : BomColumnKind.None;
    }
  }
}
