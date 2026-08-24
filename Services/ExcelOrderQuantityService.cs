using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal sealed class ExcelOrderQuantityRow
{
  public string DrawingNumber { get; init; } = string.Empty;
  public int Quantity { get; init; }
  public int? LineNumber { get; init; }
  public string ArticleNo { get; init; } = string.Empty;
  public string Category { get; init; } = string.Empty;
  public string Description { get; init; } = string.Empty;
  public bool IsPipe { get; init; }
  public PipeProfileDefinition? Profile { get; init; }
  public double? LengthMm { get; init; }
}

internal static class ExcelOrderQuantityService
{
  private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

  public static string? FindExcelFile(string searchRoot)
  {
    if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
      return null;

    var files = Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories)
      .Where(path =>
      {
        var ext = Path.GetExtension(path);
        return ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".xls", StringComparison.OrdinalIgnoreCase);
      })
      .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (files.Count == 0)
      return null;

    var preferred = files.FirstOrDefault(path =>
    {
      var name = Path.GetFileName(path);
      return name.Contains("Daten", StringComparison.OrdinalIgnoreCase)
             || name.Contains("Bestell", StringComparison.OrdinalIgnoreCase)
             || name.Contains("Order", StringComparison.OrdinalIgnoreCase)
             || name.Contains("Menge", StringComparison.OrdinalIgnoreCase);
    });

    return preferred ?? files[0];
  }

  public static IReadOnlyList<ExcelOrderQuantityRow> LoadQuantities(string excelPath)
  {
    var extension = Path.GetExtension(excelPath);
    if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
      return LoadFromXlsx(excelPath);

    throw new NotSupportedException(
      "Excel-Format ." + extension.TrimStart('.') + " wird noch nicht unterstützt. Bitte .xlsx oder .xlsm verwenden.");
  }

  public static int? FindQuantityForDrawing(IReadOnlyList<ExcelOrderQuantityRow> rows, string pdfFileName)
  {
    if (rows.Count == 0 || string.IsNullOrWhiteSpace(pdfFileName))
      return null;

    ExcelOrderQuantityRow? best = null;
    var bestScore = 0;

    foreach (var row in rows)
    {
      if (row.Quantity <= 0)
        continue;

      var score = ScoreDrawingMatch(row.DrawingNumber, pdfFileName);
      if (score > bestScore)
      {
        bestScore = score;
        best = row;
      }
    }

    return bestScore >= 60 ? best?.Quantity : null;
  }

  public static bool MatchesDrawing(string drawingNumber, string pdfFileName) =>
    ScoreDrawingMatch(drawingNumber, pdfFileName) >= 60;

  private static int ScoreDrawingMatch(string drawingNumber, string pdfFileName)
  {
    if (string.IsNullOrWhiteSpace(drawingNumber) || string.IsNullOrWhiteSpace(pdfFileName))
      return 0;

    var pdfKey = NormalizeKey(StripRevision(Path.GetFileNameWithoutExtension(pdfFileName)));
    var drawingKey = NormalizeKey(StripRevision(drawingNumber));
    if (string.IsNullOrWhiteSpace(pdfKey) || string.IsNullOrWhiteSpace(drawingKey))
      return 0;

    if (string.Equals(drawingKey, pdfKey, StringComparison.OrdinalIgnoreCase))
      return 100;
    if (pdfKey.Contains(drawingKey, StringComparison.OrdinalIgnoreCase))
      return 80;
    if (drawingKey.Contains(pdfKey, StringComparison.OrdinalIgnoreCase))
      return 70;

    var pdfCore = StripLeadingPrefix(pdfKey);
    var drawingCore = StripLeadingPrefix(drawingKey);
    if (!string.IsNullOrWhiteSpace(pdfCore)
        && !string.IsNullOrWhiteSpace(drawingCore)
        && (pdfCore.Contains(drawingCore, StringComparison.OrdinalIgnoreCase)
            || drawingCore.Contains(pdfCore, StringComparison.OrdinalIgnoreCase)))
      return 60;

    return 0;
  }

  private static IReadOnlyList<ExcelOrderQuantityRow> LoadFromXlsx(string excelPath)
  {
    using var archive = ZipFile.OpenRead(excelPath);
    var workbook = LoadXml(archive, "xl/workbook.xml");
    var relsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
    var sharedStrings = LoadSharedStrings(archive);
    var result = new List<ExcelOrderQuantityRow>();

    var rels = relsXml.Descendants()
      .Where(e => e.Name.LocalName == "Relationship")
      .ToDictionary(
        e => (string?)e.Attribute("Id") ?? string.Empty,
        e => (string?)e.Attribute("Target") ?? string.Empty,
        StringComparer.OrdinalIgnoreCase);

    foreach (var sheetEl in workbook.Descendants(SpreadsheetNs + "sheet"))
    {
      var relId = sheetEl.Attribute(RelationshipNs + "id")?.Value;
      if (string.IsNullOrWhiteSpace(relId) || !rels.TryGetValue(relId, out var target))
        continue;

      var sheetPath = NormalizeZipPath("xl/" + target.TrimStart('/'));
      var sheetXml = LoadXml(archive, sheetPath);
      var allRows = sheetXml.Descendants(SpreadsheetNs + "row").ToList();

      string? drawingCol = null;
      string? amountCol = null;
      string? descriptionCol = null;
      string? categoryCol = null;
      string? articleCol = null;
      string? lineCol = null;
      var headerRowNumber = -1;

      foreach (var row in allRows)
      {
        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
          var text = GetCellText(cell, sharedStrings).Trim();
          var col = GetColumnRef((string?)cell.Attribute("r"));
          if (drawingCol is null && IsDrawingHeader(text))
          {
            drawingCol = col;
            headerRowNumber = GetRowNumber(row);
          }

          if (descriptionCol is null && IsDescriptionHeader(text))
          {
            descriptionCol = col;
            if (headerRowNumber < 0)
              headerRowNumber = GetRowNumber(row);
          }
        }
      }

      if (headerRowNumber > 0)
      {
        foreach (var row in allRows.Where(r =>
                 {
                   var n = GetRowNumber(r);
                   return n == headerRowNumber || n == headerRowNumber + 1 || n == headerRowNumber + 2;
                 }))
        {
          foreach (var cell in row.Elements(SpreadsheetNs + "c"))
          {
            var text = GetCellText(cell, sharedStrings).Trim();
            var col = GetColumnRef((string?)cell.Attribute("r"));
            if (amountCol is null && IsAmountHeader(text))
              amountCol = col;
            if (categoryCol is null && IsCategoryHeader(text))
              categoryCol = col;
            if (articleCol is null && IsArticleHeader(text))
              articleCol = col;
            if (lineCol is null && IsLineHeader(text))
              lineCol = col;
            if (descriptionCol is null && IsDescriptionHeader(text))
              descriptionCol = col;
          }
        }
      }

      if (amountCol is null || (drawingCol is null && descriptionCol is null))
        continue;

      foreach (var row in allRows)
      {
        if (GetRowNumber(row) <= headerRowNumber)
          continue;

        var drawing = drawingCol is null ? string.Empty : GetCellValueByCol(row, drawingCol, sharedStrings).Trim();
        var description = descriptionCol is null ? string.Empty : GetCellValueByCol(row, descriptionCol, sharedStrings).Trim();
        var category = categoryCol is null ? string.Empty : GetCellValueByCol(row, categoryCol, sharedStrings).Trim();
        var article = articleCol is null ? string.Empty : GetCellValueByCol(row, articleCol, sharedStrings).Trim();
        var lineRaw = lineCol is null ? string.Empty : GetCellValueByCol(row, lineCol, sharedStrings).Trim();

        if (string.IsNullOrWhiteSpace(drawing) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(category))
          continue;

        var amountRaw = GetCellValueByCol(row, amountCol, sharedStrings).Trim();
        if (!TryParseQuantity(amountRaw, out var quantity))
          continue;

        int? lineNumber = null;
        if (int.TryParse(lineRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLine)
            && parsedLine > 0)
          lineNumber = parsedLine;

        var profile = PipePartParser.TryParseProfile(description)
                      ?? PipePartParser.TryParseProfile(category);
        var isPipe = PipePartParser.IsPipeDescription(description)
                     || PipePartParser.IsPipeDescription(category);

        result.Add(new ExcelOrderQuantityRow
        {
          DrawingNumber = drawing,
          Quantity = quantity,
          LineNumber = lineNumber,
          ArticleNo = article,
          Category = category,
          Description = description,
          IsPipe = isPipe,
          Profile = profile,
          LengthMm = PipePartParser.TryParseLengthMm(description, profile)
        });
      }
    }

    return result;
  }

  private static bool TryParseQuantity(string raw, out int quantity)
  {
    quantity = 0;
    if (string.IsNullOrWhiteSpace(raw))
      return false;

    var cleaned = raw.Replace(',', '.').Trim();
    if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value > 0
        && value < 100000)
    {
      quantity = (int)Math.Round(value);
      return quantity > 0;
    }

    var digits = Regex.Match(cleaned, @"\d+");
    if (digits.Success && int.TryParse(digits.Value, out quantity) && quantity > 0)
      return true;

    return false;
  }

  private static bool IsDrawingHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "drawing" or "zeichnung" or "zeichnungsnr" or "zeichnungsnummer" or "drawingno" or "drawingnumber"
           || n.Contains("zeichnung")
           || (n.Contains("drawing") && !n.Contains("date") && !n.Contains("rev"));
  }

  private static bool IsDescriptionHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "description" or "benennung" or "bezeichnung" or "beschr" or "desc"
           || n.Contains("benennung")
           || n.Contains("bezeichnung")
           || (n.Contains("description") && !n.Contains("date"));
  }

  private static bool IsCategoryHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "articlecategory" or "category" or "kategorie" or "warengruppe" or "cat";
  }

  private static bool IsArticleHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "articleno" or "articlenumber" or "artikel" or "artikelnummer" or "sachnummer" or "artikelnr";
  }

  private static bool IsLineHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "linenumber" or "line" or "lfd" or "lfdnr" or "pos" or "position" or "posnr";
  }

  private static bool IsAmountHeader(string value)
  {
    var n = NormalizeHeader(value);
    return n is "qty" or "quantity" or "menge" or "stück" or "stuck" or "stückzahl" or "stuckzahl" or "anzahl"
           || n.Contains("bestellmenge")
           || n.Contains("requestedamount")
           || n.Contains("orderqty")
           || (n.Contains("amount") && !n.Contains("date"))
           || (n.Contains("menge") && !n.Contains("gewicht"));
  }

  private static string NormalizeHeader(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return string.Empty;

    var builder = new StringBuilder(value.Length);
    foreach (var ch in value.Trim().ToLowerInvariant())
    {
      if (char.IsLetterOrDigit(ch))
        builder.Append(ch);
    }

    return builder.ToString();
  }

  private static string NormalizeKey(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return string.Empty;

    var builder = new StringBuilder(value.Length);
    foreach (var ch in value.ToUpperInvariant())
    {
      if (char.IsLetterOrDigit(ch))
        builder.Append(ch);
    }

    return builder.ToString();
  }

  private static string StripRevision(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return string.Empty;

    return Regex.Replace(
      value.Trim(),
      @"([_\-\s]?(?:REV|Rev|rev)[_\-\s]?\d+)$",
      string.Empty,
      RegexOptions.CultureInvariant);
  }

  private static string StripLeadingPrefix(string key)
  {
    if (string.IsNullOrWhiteSpace(key))
      return string.Empty;

    var match = Regex.Match(key, @"^[A-Z]{1,5}(\d.+)$");
    return match.Success ? match.Groups[1].Value : key;
  }

  private static List<string> LoadSharedStrings(ZipArchive archive)
  {
    var entry = archive.GetEntry("xl/sharedStrings.xml");
    if (entry is null)
      return [];

    using var stream = entry.Open();
    var doc = XDocument.Load(stream);
    return doc.Descendants(SpreadsheetNs + "si")
      .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
      .ToList();
  }

  private static XDocument LoadXml(ZipArchive archive, string path)
  {
    var entry = archive.GetEntry(path.Replace('\\', '/'))
                ?? throw new InvalidOperationException("Excel-Datei unvollständig: " + path);
    using var stream = entry.Open();
    return XDocument.Load(stream);
  }

  private static string NormalizeZipPath(string path) =>
    path.Replace('\\', '/').Replace("//", "/");

  private static int GetRowNumber(XElement row) =>
    int.TryParse((string?)row.Attribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
      ? n
      : 0;

  private static string GetColumnRef(string? cellRef)
  {
    if (string.IsNullOrWhiteSpace(cellRef))
      return string.Empty;

    return new string(cellRef.TakeWhile(char.IsLetter).ToArray());
  }

  private static string GetCellValueByCol(XElement row, string colRef, IList<string> sharedStrings)
  {
    foreach (var cell in row.Elements(SpreadsheetNs + "c"))
    {
      var cellCol = GetColumnRef((string?)cell.Attribute("r"));
      if (string.Equals(cellCol, colRef, StringComparison.OrdinalIgnoreCase))
        return GetCellText(cell, sharedStrings);
    }

    return string.Empty;
  }

  private static string GetCellText(XElement cell, IList<string> sharedStrings)
  {
    var type = (string?)cell.Attribute("t");
    var value = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
    if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
        && index >= 0
        && index < sharedStrings.Count)
      return sharedStrings[index];

    if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
      return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(t => t.Value));

    return value;
  }
}
