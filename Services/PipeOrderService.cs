using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PipeOrderService
{
  public static PipeOrderRecord SaveFromOptimization(
    string orderReference,
    PipeProfileDefinition profile,
    string material,
    double stockLengthMm,
    double kerfMm,
    IReadOnlyList<CutPartEntry> parts,
    CutOptimizationResult result,
    WarehouseReservationResult? reservation,
    bool warehouseBooked)
  {
    var orders = PipeOrderStore.Load();
    var existing = orders.FirstOrDefault(order =>
      string.Equals(order.OrderReference, orderReference, StringComparison.OrdinalIgnoreCase));

    var orderLines = PipeWarehouseService.BuildOrderList(profile.Id, material, result, profile);
    var now = DateTime.UtcNow;

    var record = existing ?? new PipeOrderRecord { OrderReference = orderReference, CreatedUtc = now };
    record.UpdatedUtc = now;
    record.ProfileId = profile.Id;
    record.ProfileLabel = profile.FullLabel;
    record.Material = material;
    record.StockLengthMm = stockLengthMm;
    record.KerfMm = kerfMm;
    record.Parts = parts.Select(ClonePart).ToList();
    record.Result = result;
    record.OrderLines = orderLines.ToList();
    record.OrderedNewBarsCount = result.OrderedNewBarsCount;
    record.WarehouseBooked = warehouseBooked;

    if (warehouseBooked)
      record.Status = PipeOrderStatus.Completed;
    else if (result.OrderedNewBarsCount > 0)
      record.Status = PipeOrderStatus.Ordered;
    else
      record.Status = PipeOrderStatus.Reserved;

    if (existing is null)
      orders.Add(record);
    else
    {
      var index = orders.FindIndex(order =>
        string.Equals(order.OrderReference, orderReference, StringComparison.OrdinalIgnoreCase));
      if (index >= 0)
        orders[index] = record;
    }

    PipeOrderStore.Save(orders);
    return record;
  }

  public static PipeOrderRecord ReceiveMaterial(string orderReference)
  {
    var order = RequireOrder(orderReference);
    if (order.Status != PipeOrderStatus.Ordered)
      throw new InvalidOperationException("Wareneingang ist nur für bestellte Aufträge möglich.");

    if (order.OrderLines.Count == 0)
      throw new InvalidOperationException("Für diesen Auftrag liegt keine Bestellmenge vor.");

    var warehouse = PipeWarehouseStore.Load();
    foreach (var line in order.OrderLines)
      AddStockQuantity(warehouse, line.ProfileId, line.Material, line.LengthMm, line.Quantity);

    PipeWarehouseStore.Save(warehouse);

    order.Status = PipeOrderStatus.MaterialReceived;
    order.UpdatedUtc = DateTime.UtcNow;
    Upsert(order);
    return order;
  }

  public static (PipeOrderRecord Order, WarehouseReservationResult Reservation) CompleteCut(string orderReference)
  {
    var order = RequireOrder(orderReference);
    if (order.WarehouseBooked)
      throw new InvalidOperationException("Der Schnitt wurde für diesen Auftrag bereits verbucht.");

    if (order.Status == PipeOrderStatus.Completed)
      throw new InvalidOperationException("Dieser Auftrag ist bereits abgeschlossen.");

    var warehouse = PipeWarehouseStore.Load();
    var reservation = PipeWarehouseService.ReserveOptimizationResult(
      order.ProfileId,
      order.Material,
      order.Result,
      warehouse,
      order.OrderReference);

    order.WarehouseBooked = true;
    order.Status = PipeOrderStatus.Completed;
    order.UpdatedUtc = DateTime.UtcNow;
    Upsert(order);

    return (order, reservation);
  }

  public static string FormatReceiveSummary(PipeOrderRecord order) =>
    "Material ins Lager gebucht:" + Environment.NewLine
    + string.Join(Environment.NewLine, order.OrderLines.Select(line => "· " + line.Summary));

  public static string FormatCompleteSummary(WarehouseReservationResult reservation) =>
    PipeWarehouseService.FormatReservationSummary(reservation);

  private static void Upsert(PipeOrderRecord order)
  {
    var orders = PipeOrderStore.Load();
    var index = orders.FindIndex(item =>
      string.Equals(item.OrderReference, order.OrderReference, StringComparison.OrdinalIgnoreCase));

    if (index >= 0)
      orders[index] = order;
    else
      orders.Add(order);

    PipeOrderStore.Save(orders);
  }

  private static PipeOrderRecord RequireOrder(string orderReference)
  {
    var order = PipeOrderStore.FindByReference(orderReference);
    if (order is null)
      throw new InvalidOperationException($"Auftrag „{orderReference}“ wurde nicht gefunden.");

    return order;
  }

  private static void AddStockQuantity(
    List<PipeWarehouseStockItem> warehouse,
    string profileId,
    string material,
    double lengthMm,
    int quantity)
  {
    if (quantity <= 0)
      return;

    var rounded = Math.Round(lengthMm, 1);
    var existing = warehouse.FirstOrDefault(item =>
      string.Equals(item.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
      && string.Equals(item.Material, material, StringComparison.OrdinalIgnoreCase)
      && Math.Abs(item.LengthMm - rounded) < 0.5);

    if (existing is not null)
    {
      existing.Quantity += quantity;
      return;
    }

    var profile = PipeStockCatalog.TryGet(profileId)
      ?? throw new InvalidOperationException($"Profil „{profileId}“ ist im Katalog nicht vorhanden.");

    var entry = new PipeWarehouseStockItem
    {
      ProfileId = profileId,
      Material = material,
      LengthMm = rounded,
      Quantity = quantity
    };
    entry.RefreshFromProfile(profile);
    warehouse.Add(entry);
  }

  private static CutPartEntry ClonePart(CutPartEntry part) =>
    new()
    {
      DrawingName = part.DrawingName,
      PdfPath = part.PdfPath,
      LengthMm = part.LengthMm,
      MiterEnd1Deg = part.MiterEnd1Deg,
      MiterEnd2Deg = part.MiterEnd2Deg,
      Quantity = part.Quantity
    };
}
