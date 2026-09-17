using System.Windows;

namespace RohreZuschnittOptimierung.Models;

public sealed class UpdateChangeLine
{
  public string Text { get; init; } = string.Empty;
  public bool IsHeader { get; init; }
  public FontWeight Weight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
  public double FontSize => IsHeader ? 13 : 12;
  public Thickness Margin { get; init; } = new(0, 0, 0, 6);
}
