using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class PipeOrdersWindow : Window
{
  private readonly ObservableCollection<PipeOrderListItem> _orders = new();

  public PipeOrdersWindow()
  {
    InitializeComponent();
    OrdersGrid.ItemsSource = _orders;
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
    ReloadOrders();
  }

  private void Reload_Click(object sender, RoutedEventArgs e) => ReloadOrders();

  private void OrdersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();

  private void ReceiveMaterial_Click(object sender, RoutedEventArgs e)
  {
    var order = GetSelectedOrder();
    if (order is null)
      return;

    var answer = MessageBox.Show(
      this,
      PipeOrderService.FormatReceiveSummary(order) + Environment.NewLine + Environment.NewLine
      + "Material jetzt ins Lager buchen?",
      $"Wareneingang — {order.OrderReference}",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
      return;

    try
    {
      PipeOrderService.ReceiveMaterial(order.OrderReference);
      ReloadOrders();
      MessageBox.Show(
        this,
        "Material wurde ins Lager gebucht." + Environment.NewLine
        + "Nach dem Schneiden: „Schnitt verbuchen“.",
        "Wareneingang",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Wareneingang", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void CompleteCut_Click(object sender, RoutedEventArgs e)
  {
    var order = GetSelectedOrder();
    if (order is null)
      return;

    var answer = MessageBox.Show(
      this,
      "Verbrauch aus dem Lager reservieren und Rohreste zurückbuchen?" + Environment.NewLine + Environment.NewLine
      + $"Auftrag: {order.OrderReference}",
      "Schnitt verbuchen",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
      return;

    try
    {
      var (_, reservation) = PipeOrderService.CompleteCut(order.OrderReference);
      ReloadOrders();
      var summary = PipeOrderService.FormatCompleteSummary(reservation);
      MessageBox.Show(
        this,
        (string.IsNullOrWhiteSpace(summary) ? "Lager wurde aktualisiert." : summary),
        "Schnitt verbucht",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Schnitt verbuchen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void ReloadOrders()
  {
    _orders.Clear();
    foreach (var order in PipeOrderStore.Load().OrderByDescending(item => item.UpdatedUtc))
      _orders.Add(PipeOrderListItem.From(order));

    UpdateActions();
  }

  private PipeOrderRecord? GetSelectedOrder()
  {
    if (OrdersGrid.SelectedItem is not PipeOrderListItem item)
    {
      MessageBox.Show(this, "Bitte zuerst einen Auftrag auswählen.", "Aufträge", MessageBoxButton.OK, MessageBoxImage.Information);
      return null;
    }

    return PipeOrderStore.FindByReference(item.OrderReference);
  }

  private void UpdateActions()
  {
    if (OrdersGrid.SelectedItem is not PipeOrderListItem item)
    {
      ReceiveMaterialButton.IsEnabled = false;
      CompleteCutButton.IsEnabled = false;
      DetailTextBlock.Text = "Auftrag auswählen.";
      return;
    }

    ReceiveMaterialButton.IsEnabled = item.Status == PipeOrderStatus.Ordered;
    CompleteCutButton.IsEnabled = !item.WarehouseBooked
      && item.Status is PipeOrderStatus.Ordered or PipeOrderStatus.MaterialReceived or PipeOrderStatus.Reserved;

    var details = $"{item.OrderReference} · {item.ProfileLabel} · {item.Material}";
    if (item.OrderedNewBarsCount > 0)
      details += Environment.NewLine + $"Bestellt: {item.OrderedNewBarsCount} Stange(n)";

    if (item.WarehouseBooked)
      details += Environment.NewLine + "Lagerbuchung: abgeschlossen";
    else if (item.Status == PipeOrderStatus.Ordered)
      details += Environment.NewLine + "Nächster Schritt: Material eingetroffen (oder manuell ins Lager), dann Schnitt verbuchen.";
    else if (item.Status == PipeOrderStatus.MaterialReceived)
      details += Environment.NewLine + "Nächster Schritt: Schnitt verbuchen.";

    DetailTextBlock.Text = details;
  }

  private sealed class PipeOrderListItem
  {
    public string OrderReference { get; init; } = string.Empty;
    public PipeOrderStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string ProfileLabel { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
    public int OrderedNewBarsCount { get; init; }
    public bool WarehouseBooked { get; init; }
    public string UpdatedLocal { get; init; } = string.Empty;

    public static PipeOrderListItem From(PipeOrderRecord order) =>
      new()
      {
        OrderReference = order.OrderReference,
        Status = order.Status,
        StatusLabel = order.StatusLabel,
        ProfileLabel = order.ProfileLabel,
        Material = order.Material,
        OrderedNewBarsCount = order.OrderedNewBarsCount,
        WarehouseBooked = order.WarehouseBooked,
        UpdatedLocal = order.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
      };
  }
}
