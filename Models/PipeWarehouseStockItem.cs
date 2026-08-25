using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RohreZuschnittOptimierung.Models;

public sealed class PipeWarehouseStockItem : INotifyPropertyChanged
{
  private bool _isSelected;
  private string _material = PipeMaterialTypes.Steel;
  private double _lengthMm;
  private int _quantity;
  private int _reservedQuantity;

  public string ProfileId { get; set; } = string.Empty;

  public string Material
  {
    get => _material;
    set
    {
      if (string.Equals(_material, value, StringComparison.Ordinal))
        return;
      _material = value ?? PipeMaterialTypes.Steel;
      OnPropertyChanged();
    }
  }

  public double LengthMm
  {
    get => _lengthMm;
    set
    {
      if (Math.Abs(_lengthMm - value) < 0.0001)
        return;
      _lengthMm = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(LengthLabel));
      OnPropertyChanged(nameof(IsOriginalStock));
      OnPropertyChanged(nameof(StockTypeLabel));
    }
  }

  public int Quantity
  {
    get => _quantity;
    set
    {
      if (_quantity == value)
        return;
      _quantity = value;
      OnPropertyChanged();
    }
  }

  public int ReservedQuantity
  {
    get => _reservedQuantity;
    set
    {
      if (_reservedQuantity == value)
        return;
      _reservedQuantity = value;
      OnPropertyChanged();
    }
  }

  public string ProfileDisplayName { get; set; } = string.Empty;
  public string ProfileKindLabel { get; set; } = string.Empty;
  public string ProfileDimensions { get; set; } = string.Empty;

  public bool IsSelected
  {
    get => _isSelected;
    set
    {
      if (_isSelected == value)
        return;

      _isSelected = value;
      OnPropertyChanged();
    }
  }

  public string LengthLabel =>
    Math.Abs(LengthMm - 6000) < 0.5 ? "6.000 mm (Original)" : $"{LengthMm:0} mm";

  public bool IsOriginalStock => Math.Abs(LengthMm - CutOptimizationDefaults.StockLengthMm) < 0.5;

  public string StockTypeLabel => IsOriginalStock ? "Neumaterial" : "Rohrest";

  public void RefreshFromProfile(PipeProfileDefinition profile)
  {
    ProfileDisplayName = profile.FullLabel;
    ProfileKindLabel = profile.KindLabel;
    ProfileDimensions = profile.Dimensions;
    if (string.IsNullOrWhiteSpace(Material))
      Material = profile.Material;
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
