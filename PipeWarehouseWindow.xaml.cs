using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class PipeWarehouseWindow : Window
{
  private enum StockFilterMode
  {
    All,
    InStockOnly,
    NewMaterialOnly,
    RemnantsOnly
  }

  private readonly ObservableCollection<PipeWarehouseStockItem> _allItems = new();
  private readonly ICollectionView _view;
  private string _textFilter = string.Empty;
  private StockFilterMode _stockFilter = StockFilterMode.All;
  private bool _syncingSelectAll;

  public PipeWarehouseWindow()
  {
    InitializeComponent();
    _view = CollectionViewSource.GetDefaultView(_allItems);
    _view.Filter = FilterItem;
    WarehouseGrid.ItemsSource = _view;

    Loaded += (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      PipeWarehouseStore.ExternalChanged += OnWarehouseExternalChanged;
    };
    Closing += WarehouseWindow_Closing;
    Closed += (_, _) => PipeWarehouseStore.ExternalChanged -= OnWarehouseExternalChanged;
    LoadItems();
    SetStockFilter(StockFilterMode.InStockOnly);
    UpdateStatus();
  }

  private void OnWarehouseExternalChanged()
  {
    Dispatcher.BeginInvoke(() =>
    {
      try
      {
        LoadItems();
        StatusTextBlock.Text += " · von anderem PC aktualisiert";
      }
      catch
      {
      }
    });
  }

  private bool OpenAddWizard(bool isNewMaterial)
  {
    var wizard = new PipeWarehouseAddWizardWindow(isNewMaterial) { Owner = this };
    if (wizard.ShowDialog() != true || wizard.Result is null)
      return false;

    var result = wizard.Result;
    AddOrIncrementStock(result.Profile, result.Material, result.LengthMm, result.Quantity);
    try
    {
      PersistWarehouse();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
      return false;
    }

    _view.Refresh();
    UpdateSelectAllCheckBox();
    UpdateStatus();
    return true;
  }

  private void AddNewMaterial_Click(object sender, RoutedEventArgs e) => OpenAddWizard(isNewMaterial: true);

  private void AddRemnant_Click(object sender, RoutedEventArgs e) => OpenAddWizard(isNewMaterial: false);

  private void LoadItems()
  {
    _allItems.Clear();
    foreach (var item in PipeWarehouseStore.Load())
    {
      var clone = Clone(item);
      clone.PropertyChanged += StockItem_PropertyChanged;
      _allItems.Add(clone);
    }

    _view.Refresh();
    UpdateSelectAllCheckBox();
    UpdateStatus();
  }

  private void StockItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(PipeWarehouseStockItem.IsSelected))
    {
      UpdateSelectAllCheckBox();
      UpdateStatus();
    }
  }

  private void WarehouseGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (WarehouseGrid.SelectedItem is not PipeWarehouseStockItem item)
      return;

    e.Handled = true;
    if (!TryPromptQuantity(item, out var quantity))
      return;

    item.Quantity = quantity;
    _view.Refresh();
    UpdateSelectAllCheckBox();
    UpdateStatus();
  }

  private bool TryPromptQuantity(PipeWarehouseStockItem item, out int quantity)
  {
    quantity = item.Quantity;

    var input = new TextBox
    {
      Text = item.Quantity.ToString(CultureInfo.CurrentCulture),
      Margin = new Thickness(16, 12, 16, 8),
      Padding = new Thickness(8, 6, 8, 6),
      MinWidth = 220
    };

    var ok = new Button
    {
      Content = "OK",
      IsDefault = true,
      MinWidth = 88,
      Margin = new Thickness(0, 0, 8, 0),
      Padding = new Thickness(12, 6, 12, 6)
    };
    var cancel = new Button
    {
      Content = "Abbrechen",
      IsCancel = true,
      MinWidth = 88,
      Padding = new Thickness(12, 6, 12, 6)
    };

    var buttons = new StackPanel
    {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Margin = new Thickness(16, 8, 16, 16)
    };
    buttons.Children.Add(ok);
    buttons.Children.Add(cancel);

    var root = new StackPanel();
    root.Children.Add(new TextBlock
    {
      Text = $"Menge für {item.ProfileDisplayName}",
      Margin = new Thickness(16, 16, 16, 0),
      TextWrapping = TextWrapping.Wrap,
      FontWeight = FontWeights.SemiBold
    });
    root.Children.Add(input);
    root.Children.Add(buttons);

    var dialog = new Window
    {
      Title = "Menge ändern",
      Owner = this,
      Content = root,
      SizeToContent = SizeToContent.WidthAndHeight,
      ResizeMode = ResizeMode.NoResize,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      ShowInTaskbar = false,
      Background = Background,
      Foreground = Foreground
    };

    var accepted = false;
    ok.Click += (_, _) =>
    {
      accepted = true;
      dialog.DialogResult = true;
      dialog.Close();
    };
    cancel.Click += (_, _) =>
    {
      dialog.DialogResult = false;
      dialog.Close();
    };

    dialog.Loaded += (_, _) =>
    {
      input.Focus();
      input.SelectAll();
    };

    if (dialog.ShowDialog() != true || !accepted)
      return false;

    if (!int.TryParse(input.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out quantity)
        && !int.TryParse(input.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
    {
      MessageBox.Show(this, "Bitte eine ganze Zahl als Menge eingeben.", "Menge ändern", MessageBoxButton.OK, MessageBoxImage.Warning);
      return false;
    }

    if (quantity < 0)
      quantity = 0;

    return true;
  }

  private IEnumerable<PipeWarehouseStockItem> GetVisibleItems() =>
    _view.Cast<PipeWarehouseStockItem>();

  private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
  {
    if (_syncingSelectAll)
      return;

    var selectAll = SelectAllCheckBox.IsChecked == true;
    foreach (var item in GetVisibleItems())
      item.IsSelected = selectAll;

    UpdateStatus();
  }

  private void UpdateSelectAllCheckBox()
  {
    var visible = GetVisibleItems().ToList();
    if (visible.Count == 0)
    {
      _syncingSelectAll = true;
      SelectAllCheckBox.IsChecked = false;
      _syncingSelectAll = false;
      return;
    }

    var selectedCount = visible.Count(item => item.IsSelected);
    _syncingSelectAll = true;
    SelectAllCheckBox.IsChecked = selectedCount switch
    {
      0 => false,
      _ when selectedCount == visible.Count => true,
      _ => null
    };
    _syncingSelectAll = false;
  }

  private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
  {
    _textFilter = FilterTextBox.Text.Trim();
    _view.Refresh();
    UpdateSelectAllCheckBox();
    UpdateStatus();
  }

  private bool FilterItem(object obj)
  {
    if (obj is not PipeWarehouseStockItem item)
      return false;

    if (_stockFilter == StockFilterMode.InStockOnly && item.Quantity <= 0)
      return false;

    if (_stockFilter == StockFilterMode.NewMaterialOnly && !item.IsOriginalStock)
      return false;

    if (_stockFilter == StockFilterMode.RemnantsOnly && item.IsOriginalStock)
      return false;

    if (string.IsNullOrWhiteSpace(_textFilter))
      return true;

    var haystack = string.Join(
      " ",
      item.StockTypeLabel,
      item.ProfileKindLabel,
      item.ProfileDisplayName,
      item.ProfileDimensions,
      item.Material,
      item.ProfileId);

    return haystack.Contains(_textFilter, StringComparison.OrdinalIgnoreCase);
  }

  private void AddOrIncrementStock(PipeProfileDefinition profile, string material, double lengthMm, int quantity)
  {
    var existing = _allItems.FirstOrDefault(item =>
      string.Equals(item.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase)
      && string.Equals(item.Material, material, StringComparison.OrdinalIgnoreCase)
      && Math.Abs(item.LengthMm - lengthMm) < 0.5);

    if (existing is not null)
    {
      existing.Quantity += quantity;
      return;
    }

    var entry = new PipeWarehouseStockItem
    {
      ProfileId = profile.Id,
      Material = material,
      LengthMm = lengthMm,
      Quantity = quantity
    };
    entry.RefreshFromProfile(profile);
    entry.PropertyChanged += StockItem_PropertyChanged;
    _allItems.Add(entry);
  }

  private void DeleteSelected_Click(object sender, RoutedEventArgs e)
  {
    var selected = _allItems.Where(item => item.IsSelected).ToList();
    if (selected.Count == 0)
    {
      MessageBox.Show(
        this,
        "Bitte mindestens eine Zeile mit Häkchen markieren.",
        "Löschen",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      return;
    }

    var answer = MessageBox.Show(
      this,
      $"{selected.Count} markierte Zeile(n) wirklich löschen?",
      "Markierte löschen",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
      return;

    foreach (var item in selected)
    {
      item.PropertyChanged -= StockItem_PropertyChanged;
      _allItems.Remove(item);
    }

    _view.Refresh();
    UpdateSelectAllCheckBox();
    UpdateStatus();
    PersistWarehouse();
  }

  private void FilterAll_Click(object sender, RoutedEventArgs e) => SetStockFilter(StockFilterMode.All);

  private void FilterInStock_Click(object sender, RoutedEventArgs e) =>
    SetStockFilter(StockFilterMode.InStockOnly);

  private void FilterNew_Click(object sender, RoutedEventArgs e) =>
    SetStockFilter(StockFilterMode.NewMaterialOnly);

  private void FilterRemnant_Click(object sender, RoutedEventArgs e) =>
    SetStockFilter(StockFilterMode.RemnantsOnly);

  private void SetStockFilter(StockFilterMode mode)
  {
    _stockFilter = mode;
    _view.Refresh();
    UpdateFilterButtons();
    UpdateSelectAllCheckBox();
    UpdateStatus();
  }

  private void UpdateFilterButtons()
  {
    ResetFilterButton(FilterAllButton);
    ResetFilterButton(FilterInStockButton);
    ResetFilterButton(FilterNewButton);
    ResetFilterButton(FilterRemnantButton);

    var active = _stockFilter switch
    {
      StockFilterMode.InStockOnly => FilterInStockButton,
      StockFilterMode.NewMaterialOnly => FilterNewButton,
      StockFilterMode.RemnantsOnly => FilterRemnantButton,
      _ => FilterAllButton
    };

    active.FontWeight = FontWeights.SemiBold;
  }

  private static void ResetFilterButton(Button button) =>
    button.FontWeight = FontWeights.Normal;

  private void RebuildWarehouse_Click(object sender, RoutedEventArgs e)
  {
    var answer = MessageBox.Show(
      this,
      $"Alle {PipeStockCatalog.All.Count} Standard-Rohrtypen werden neu angelegt (6.000 mm, Bestand 0).\n\nBestehende Einträge werden ersetzt. Fortfahren?",
      "Lager neu anlegen",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
      return;

    PipeWarehouseStore.InitializeWithAllProfiles();
    LoadItems();
    MessageBox.Show(
      this,
      $"Lager mit {PipeStockCatalog.All.Count} Rohrtypen angelegt.\nÜber „+ Neumaterial“ und „+ Rohrest“ Bestand eintragen.",
      "Lager neu anlegen",
      MessageBoxButton.OK,
      MessageBoxImage.Information);
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      PersistWarehouse();
      LoadItems();
      MessageBox.Show(this, "Lager gespeichert.", "Lagerverwaltung", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void PersistWarehouse()
  {
    CommitGridEdits();

    var items = _allItems
      .Where(item => !string.IsNullOrWhiteSpace(item.ProfileId) && item.LengthMm > 0)
      .Select(Clone)
      .ToList();

    PipeWarehouseStore.RefreshDisplayNames(items);
    PipeWarehouseStore.Save(items);
  }

  private void CommitGridEdits()
  {
    WarehouseGrid.CommitEdit(DataGridEditingUnit.Cell, true);
    WarehouseGrid.CommitEdit(DataGridEditingUnit.Row, true);
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();

  private void WarehouseWindow_Closing(object? sender, CancelEventArgs e)
  {
    try
    {
      PersistWarehouse();
    }
    catch (Exception ex)
    {
      var answer = MessageBox.Show(
        this,
        "Lager konnte nicht gespeichert werden:\n" + ex.Message + "\n\nTrotzdem schließen?",
        "Lagerverwaltung",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
      if (answer != MessageBoxResult.Yes)
        e.Cancel = true;
    }
  }

  private void UpdateStatus()
  {
    var visible = _view.Cast<object>().Count();
    var checkedCount = _allItems.Count(item => item.IsSelected);
    var totalQty = _allItems.Where(item => item.Quantity > 0).Sum(item => item.Quantity);
    StatusTextBlock.Text =
      $"{visible} sichtbar · {checkedCount} markiert · {totalQty} Stück mit Bestand";
  }

  private static PipeWarehouseStockItem Clone(PipeWarehouseStockItem item) =>
    new()
    {
      ProfileId = item.ProfileId,
      Material = item.Material,
      LengthMm = item.LengthMm,
      Quantity = item.Quantity,
      ReservedQuantity = item.ReservedQuantity,
      ProfileDisplayName = item.ProfileDisplayName,
      ProfileKindLabel = item.ProfileKindLabel,
      ProfileDimensions = item.ProfileDimensions
    };
}
