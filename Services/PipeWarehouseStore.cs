using System.IO;
using System.Text;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PipeWarehouseStore
{
  private const string FileName = "pipe-warehouse.xml";
  private const double DefaultStockLengthMm = CutOptimizationDefaults.StockLengthMm;

  public static string FilePath =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Rohre-Zuschnitt-Optimierung",
      FileName);

  public static List<PipeWarehouseStockItem> Load()
  {
    EnsureInitialized();
    return LoadCore();
  }

  public static void Save(IEnumerable<PipeWarehouseStockItem> items)
  {
    var directory = Path.GetDirectoryName(FilePath)!;
    Directory.CreateDirectory(directory);

    var root = new XElement(
      "PipeWarehouse",
      items
        .Where(item => !string.IsNullOrWhiteSpace(item.ProfileId) && item.LengthMm > 0)
        .OrderBy(item => item.ProfileKindLabel, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.ProfileDimensions, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.LengthMm)
        .Select(item => new XElement(
          "Stock",
          new XAttribute("profileId", item.ProfileId),
          new XAttribute("material", item.Material ?? PipeMaterialTypes.Steel),
          new XAttribute("lengthMm", item.LengthMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
          new XAttribute("quantity", item.Quantity),
          new XAttribute("reservedQuantity", item.ReservedQuantity))));

    root.Save(FilePath);
  }

  public static void EnsureInitialized()
  {
    if (File.Exists(FilePath))
      return;

    InitializeWithAllProfiles();
  }

  public static void InitializeWithAllProfiles(int defaultOriginalQuantity = 0)
  {
    var items = PipeStockCatalog.All
      .Select(profile => new PipeWarehouseStockItem
      {
        ProfileId = profile.Id,
        Material = profile.Material,
        LengthMm = DefaultStockLengthMm,
        Quantity = defaultOriginalQuantity
      })
      .ToList();

    RefreshDisplayNames(items);
    Save(items);
  }

  public static void RefreshDisplayNames(IEnumerable<PipeWarehouseStockItem> items)
  {
    foreach (var item in items)
    {
      var profile = PipeStockCatalog.TryGet(item.ProfileId);
      if (profile is not null)
        item.RefreshFromProfile(profile);
    }
  }

  private static List<PipeWarehouseStockItem> LoadCore()
  {
    if (!File.Exists(FilePath))
      return [];

    var document = XDocument.Load(FilePath);
    var items = document.Root?
      .Elements("Stock")
      .Select(element => new PipeWarehouseStockItem
      {
        ProfileId = (string?)element.Attribute("profileId") ?? string.Empty,
        Material = (string?)element.Attribute("material") ?? PipeMaterialTypes.Steel,
        LengthMm = double.TryParse(
          (string?)element.Attribute("lengthMm"),
          System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture,
          out var length)
          ? length
          : 0,
        Quantity = int.TryParse((string?)element.Attribute("quantity"), out var quantity) ? quantity : 0,
        ReservedQuantity = int.TryParse((string?)element.Attribute("reservedQuantity"), out var reserved) ? reserved : 0
      })
      .Where(item => !string.IsNullOrWhiteSpace(item.ProfileId) && item.LengthMm > 0)
      .ToList() ?? [];

    RefreshDisplayNames(items);
    return items;
  }
}
