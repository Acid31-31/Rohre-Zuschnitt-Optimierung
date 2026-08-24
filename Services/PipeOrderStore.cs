using System.Globalization;
using System.IO;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PipeOrderStore
{
  private const string FileName = "pipe-orders.xml";

  public static string FilePath =>
    Path.Combine(AppInfo.UserDataDirectory, FileName);

  public static List<PipeOrderRecord> Load()
  {
    if (!File.Exists(FilePath))
      return [];

    try
    {
      var document = XDocument.Load(FilePath);
      return document.Root?
        .Elements("Order")
        .Select(ReadOrder)
        .Where(order => !string.IsNullOrWhiteSpace(order.OrderReference))
        .ToList() ?? [];
    }
    catch
    {
      return [];
    }
  }

  public static void Save(IEnumerable<PipeOrderRecord> orders)
  {
    var directory = Path.GetDirectoryName(FilePath)!;
    Directory.CreateDirectory(directory);

    var root = new XElement(
      "PipeOrders",
      orders
        .OrderByDescending(order => order.UpdatedUtc)
        .Select(WriteOrder));

    root.Save(FilePath);
  }

  public static PipeOrderRecord? FindByReference(string orderReference) =>
    Load().FirstOrDefault(order =>
      string.Equals(order.OrderReference, orderReference, StringComparison.OrdinalIgnoreCase));

  private static PipeOrderRecord ReadOrder(XElement element)
  {
    var order = new PipeOrderRecord
    {
      OrderReference = (string?)element.Attribute("reference") ?? string.Empty,
      Status = Enum.TryParse<PipeOrderStatus>((string?)element.Attribute("status"), out var status)
        ? status
        : PipeOrderStatus.Reserved,
      CreatedUtc = ReadDateTime((string?)element.Attribute("createdUtc")),
      UpdatedUtc = ReadDateTime((string?)element.Attribute("updatedUtc")),
      ProfileId = (string?)element.Element("ProfileId") ?? string.Empty,
      ProfileLabel = (string?)element.Element("ProfileLabel") ?? string.Empty,
      Material = (string?)element.Element("Material") ?? PipeMaterialTypes.Steel,
      StockLengthMm = ReadDouble((string?)element.Element("StockLengthMm")),
      KerfMm = ReadDouble((string?)element.Element("KerfMm")),
      OrderedNewBarsCount = int.TryParse((string?)element.Element("OrderedNewBarsCount"), out var ordered) ? ordered : 0,
      WarehouseBooked = bool.TryParse((string?)element.Element("WarehouseBooked"), out var booked) && booked,
      Parts = element.Element("Parts")?.Elements("Part").Select(ReadPart).ToList() ?? [],
      OrderLines = element.Element("OrderLines")?.Elements("Line").Select(ReadOrderLine).ToList() ?? [],
      Result = ReadResult(element.Element("Result"))
    };

    return order;
  }

  private static XElement WriteOrder(PipeOrderRecord order) =>
    new(
      "Order",
      new XAttribute("reference", order.OrderReference),
      new XAttribute("status", order.Status.ToString()),
      new XAttribute("createdUtc", order.CreatedUtc.ToString("o", CultureInfo.InvariantCulture)),
      new XAttribute("updatedUtc", order.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)),
      new XElement("ProfileId", order.ProfileId),
      new XElement("ProfileLabel", order.ProfileLabel),
      new XElement("Material", order.Material),
      new XElement("StockLengthMm", order.StockLengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XElement("KerfMm", order.KerfMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XElement("OrderedNewBarsCount", order.OrderedNewBarsCount),
      new XElement("WarehouseBooked", order.WarehouseBooked.ToString().ToLowerInvariant()),
      new XElement("Parts", order.Parts.Select(WritePart)),
      new XElement("OrderLines", order.OrderLines.Select(WriteOrderLine)),
      WriteResult(order.Result));

  private static CutPartEntry ReadPart(XElement element) =>
    new()
    {
      DrawingName = (string?)element.Attribute("drawing"),
      PdfPath = (string?)element.Attribute("pdfPath"),
      LengthMm = ReadDouble((string?)element.Attribute("lengthMm")),
      MiterEnd1Deg = ReadDouble((string?)element.Attribute("miter1")),
      MiterEnd2Deg = ReadDouble((string?)element.Attribute("miter2")),
      Quantity = int.TryParse((string?)element.Attribute("quantity"), out var quantity) ? quantity : 1
    };

  private static XElement WritePart(CutPartEntry part) =>
    new(
      "Part",
      new XAttribute("drawing", part.DrawingName ?? string.Empty),
      new XAttribute("pdfPath", part.PdfPath ?? string.Empty),
      new XAttribute("lengthMm", part.LengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("miter1", part.MiterEnd1Deg.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("miter2", part.MiterEnd2Deg.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("quantity", part.Quantity));

  private static PipeOrderLine ReadOrderLine(XElement element) =>
    new()
    {
      ProfileId = (string?)element.Attribute("profileId") ?? string.Empty,
      ProfileLabel = (string?)element.Attribute("profileLabel") ?? string.Empty,
      Material = (string?)element.Attribute("material") ?? PipeMaterialTypes.Steel,
      LengthMm = ReadDouble((string?)element.Attribute("lengthMm")),
      Quantity = int.TryParse((string?)element.Attribute("quantity"), out var quantity) ? quantity : 0
    };

  private static XElement WriteOrderLine(PipeOrderLine line) =>
    new(
      "Line",
      new XAttribute("profileId", line.ProfileId),
      new XAttribute("profileLabel", line.ProfileLabel),
      new XAttribute("material", line.Material),
      new XAttribute("lengthMm", line.LengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("quantity", line.Quantity));

  private static CutOptimizationResult ReadResult(XElement? element)
  {
    if (element is null)
      return new CutOptimizationResult();

    return new CutOptimizationResult
    {
      StockLengthMm = ReadDouble((string?)element.Attribute("stockLengthMm")),
      KerfMm = ReadDouble((string?)element.Attribute("kerfMm")),
      TotalBars = int.TryParse((string?)element.Attribute("totalBars"), out var totalBars) ? totalBars : 0,
      TotalWasteMm = ReadDouble((string?)element.Attribute("totalWasteMm")),
      RemnantBarsUsed = int.TryParse((string?)element.Attribute("remnantBarsUsed"), out var remnantBars) ? remnantBars : 0,
      NewOriginalBarsUsed = int.TryParse((string?)element.Attribute("newOriginalBarsUsed"), out var newBars) ? newBars : 0,
      OrderedNewBarsCount = int.TryParse((string?)element.Attribute("orderedNewBarsCount"), out var ordered) ? ordered : 0,
      SawAdjustments = int.TryParse((string?)element.Attribute("sawAdjustments"), out var saw) ? saw : 0,
      SawPlanSummary = (string?)element.Attribute("sawPlanSummary") ?? string.Empty,
      Bars = element.Elements("Bar").Select(ReadBar).ToList()
    };
  }

  private static XElement WriteResult(CutOptimizationResult result) =>
    new(
      "Result",
      new XAttribute("stockLengthMm", result.StockLengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("kerfMm", result.KerfMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("totalBars", result.TotalBars),
      new XAttribute("totalWasteMm", result.TotalWasteMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("remnantBarsUsed", result.RemnantBarsUsed),
      new XAttribute("newOriginalBarsUsed", result.NewOriginalBarsUsed),
      new XAttribute("orderedNewBarsCount", result.OrderedNewBarsCount),
      new XAttribute("sawAdjustments", result.SawAdjustments),
      new XAttribute("sawPlanSummary", result.SawPlanSummary ?? string.Empty),
      result.Bars.Select(WriteBar));

  private static CutBarPlan ReadBar(XElement element) =>
    new()
    {
      BarNumber = int.TryParse((string?)element.Attribute("number"), out var number) ? number : 0,
      StockLengthMm = ReadDouble((string?)element.Attribute("stockLengthMm")),
      IsRemnant = bool.TryParse((string?)element.Attribute("isRemnant"), out var isRemnant) && isRemnant,
      UsedMm = ReadDouble((string?)element.Attribute("usedMm")),
      WasteMm = ReadDouble((string?)element.Attribute("wasteMm")),
      SawAdjustments = int.TryParse((string?)element.Attribute("sawAdjustments"), out var saw) ? saw : 0,
      ExternalMiterOps = int.TryParse((string?)element.Attribute("externalMiterOps"), out var miter) ? miter : 0,
      SawPlanSummary = (string?)element.Attribute("sawPlanSummary") ?? string.Empty,
      Pieces = element.Elements("Piece").Select(ReadPiece).ToList()
    };

  private static XElement WriteBar(CutBarPlan bar) =>
    new(
      "Bar",
      new XAttribute("number", bar.BarNumber),
      new XAttribute("stockLengthMm", bar.StockLengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("isRemnant", bar.IsRemnant.ToString().ToLowerInvariant()),
      new XAttribute("usedMm", bar.UsedMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("wasteMm", bar.WasteMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("sawAdjustments", bar.SawAdjustments),
      new XAttribute("externalMiterOps", bar.ExternalMiterOps),
      new XAttribute("sawPlanSummary", bar.SawPlanSummary ?? string.Empty),
      bar.Pieces.Select(WritePiece));

  private static CutPieceInstance ReadPiece(XElement element) =>
    new()
    {
      LengthMm = ReadDouble((string?)element.Attribute("lengthMm")),
      DrawingName = (string?)element.Attribute("drawing"),
      MiterEnd1Deg = ReadDouble((string?)element.Attribute("miter1")),
      MiterEnd2Deg = ReadDouble((string?)element.Attribute("miter2"))
    };

  private static XElement WritePiece(CutPieceInstance piece) =>
    new(
      "Piece",
      new XAttribute("lengthMm", piece.LengthMm.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("drawing", piece.DrawingName ?? string.Empty),
      new XAttribute("miter1", piece.MiterEnd1Deg.ToString("0.###", CultureInfo.InvariantCulture)),
      new XAttribute("miter2", piece.MiterEnd2Deg.ToString("0.###", CultureInfo.InvariantCulture)));

  private static double ReadDouble(string? value) =>
    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

  private static DateTime ReadDateTime(string? value) =>
    DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
      ? result
      : DateTime.UtcNow;
}
