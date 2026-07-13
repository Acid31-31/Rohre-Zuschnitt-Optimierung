using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PipeWarehouseService
{
  private const double RemnantReturnThresholdMm = 150;

  public static List<StockRemnantEntry> BuildStockForOptimization(
    string profileId,
    string material,
    double originalStockLengthMm,
    IReadOnlyList<PipeWarehouseStockItem> warehouse)
  {
    var profileItems = warehouse
      .Where(item => string.Equals(item.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(item.Material, material, StringComparison.OrdinalIgnoreCase)
                     && item.Quantity > 0)
      .ToList();

    var stock = new List<StockRemnantEntry>();

    foreach (var item in profileItems.OrderBy(i => i.LengthMm))
    {
      stock.Add(new StockRemnantEntry
      {
        LengthMm = item.LengthMm,
        Quantity = item.Quantity,
        IsFullBar = Math.Abs(item.LengthMm - originalStockLengthMm) < 0.5
      });
    }

    return stock;
  }

  public static List<PipeOrderLine> BuildOrderList(
    string profileId,
    string material,
    CutOptimizationResult result,
    PipeProfileDefinition profile)
  {
    if (result.OrderedNewBarsCount <= 0)
      return [];

    return
    [
      new PipeOrderLine
      {
        ProfileId = profileId,
        ProfileLabel = profile.FullLabel,
        Material = material,
        LengthMm = result.StockLengthMm,
        Quantity = result.OrderedNewBarsCount
      }
    ];
  }

  public static WarehouseReservationResult ReserveOptimizationResult(
    string profileId,
    string material,
    CutOptimizationResult result,
    List<PipeWarehouseStockItem> warehouse,
    string orderReference)
  {
    var profileItems = warehouse
      .Where(item => string.Equals(item.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(item.Material, material, StringComparison.OrdinalIgnoreCase))
      .ToList();

    var reservedByKey = new Dictionary<string, ReservedStockLine>();
    var returnedByKey = new Dictionary<string, ReservedStockLine>();

    foreach (var bar in result.Bars)
    {
      var lengthMm = bar.IsRemnant ? bar.StockLengthMm : result.StockLengthMm;
      var reservedFromWarehouse = TryReserveOne(profileItems, lengthMm);

      if (reservedFromWarehouse)
      {
        var rounded = Math.Round(lengthMm, 1);
        var isFullBar = !bar.IsRemnant;
        var key = $"{rounded:0.###}:{isFullBar}";

        if (reservedByKey.TryGetValue(key, out var existing))
        {
          reservedByKey[key] = new ReservedStockLine
          {
            LengthMm = existing.LengthMm,
            Quantity = existing.Quantity + 1,
            IsFullBar = existing.IsFullBar
          };
        }
        else
        {
          reservedByKey[key] = new ReservedStockLine
          {
            LengthMm = rounded,
            Quantity = 1,
            IsFullBar = isFullBar
          };
        }
      }

      if (reservedFromWarehouse && bar.WasteMm >= RemnantReturnThresholdMm)
      {
        AddOrIncrementRemnant(profileItems, profileId, material, bar.WasteMm, warehouse);

        var wasteRounded = Math.Round(bar.WasteMm, 1);
        var wasteKey = $"{wasteRounded:0.###}:false";
        if (returnedByKey.TryGetValue(wasteKey, out var wasteExisting))
        {
          returnedByKey[wasteKey] = new ReservedStockLine
          {
            LengthMm = wasteExisting.LengthMm,
            Quantity = wasteExisting.Quantity + 1,
            IsFullBar = false
          };
        }
        else
        {
          returnedByKey[wasteKey] = new ReservedStockLine
          {
            LengthMm = wasteRounded,
            Quantity = 1,
            IsFullBar = false
          };
        }
      }
    }

    PipeWarehouseStore.Save(warehouse);

    return new WarehouseReservationResult
    {
      OrderReference = orderReference,
      ReservedLines = reservedByKey.Values
        .OrderBy(line => line.IsFullBar ? 1 : 0)
        .ThenBy(line => line.LengthMm)
        .ToList(),
      ReturnedRemnantLines = returnedByKey.Values
        .OrderBy(line => line.LengthMm)
        .ToList()
    };
  }

  public static string FormatOrderList(IReadOnlyList<PipeOrderLine> lines)
  {
    if (lines.Count == 0)
      return string.Empty;

    return "Bestellliste:" + Environment.NewLine
           + string.Join(Environment.NewLine, lines.Select(line => "· " + line.Summary));
  }

  public static string FormatReservationSummary(WarehouseReservationResult reservation)
  {
    var parts = new List<string>();

    if (reservation.ReservedBarsCount > 0)
    {
      parts.Add("Aus Lager reserviert:");
      parts.AddRange(reservation.ReservedLines.Select(line => "· " + line.Summary));
    }

    if (reservation.ReturnedRemnantCount > 0)
    {
      if (parts.Count > 0)
        parts.Add(string.Empty);

      parts.Add("Rohrest ins Lager zurück:");
      parts.AddRange(reservation.ReturnedRemnantLines.Select(line => "· " + line.Summary));
    }

    return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
  }

  private static void AddOrIncrementRemnant(
    List<PipeWarehouseStockItem> profileItems,
    string profileId,
    string material,
    double remnantLengthMm,
    List<PipeWarehouseStockItem> warehouse)
  {
    var rounded = Math.Round(remnantLengthMm, 1);
    var existing = profileItems.FirstOrDefault(item => Math.Abs(item.LengthMm - rounded) < 0.5);
    if (existing is not null)
    {
      existing.Quantity++;
      return;
    }

    var profile = PipeStockCatalog.TryGet(profileId);
    if (profile is null)
      return;

    var entry = new PipeWarehouseStockItem
    {
      ProfileId = profileId,
      Material = material,
      LengthMm = rounded,
      Quantity = 1
    };
    entry.RefreshFromProfile(profile);
    warehouse.Add(entry);
    profileItems.Add(entry);
  }

  private static bool TryReserveOne(List<PipeWarehouseStockItem> profileItems, double lengthMm)
  {
    var item = profileItems
      .Where(stock => stock.Quantity > 0)
      .OrderBy(stock => Math.Abs(stock.LengthMm - lengthMm))
      .FirstOrDefault(stock => Math.Abs(stock.LengthMm - lengthMm) < 0.5);

    if (item is null)
      return false;

    item.Quantity = Math.Max(0, item.Quantity - 1);
    item.ReservedQuantity++;
    return true;
  }
}
