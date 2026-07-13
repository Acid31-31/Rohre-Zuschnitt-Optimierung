using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RohreZuschnittOptimierung.Models;

public sealed class PipeWarehouseStockItem : INotifyPropertyChanged
{
  private bool _isSelected;

  public string ProfileId { get; set; } = string.Empty;
  public string Material { get; set; } = PipeMaterialTypes.Steel;
  public double LengthMm { get; set; }
  public int Quantity { get; set; }
  public int ReservedQuantity { get; set; }

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
