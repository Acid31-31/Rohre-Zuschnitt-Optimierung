using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class LicenseKeyWindow : Window
{
  public LicenseKeyWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    ThisPcCodeTextBox.Text = LicenseActivationService.GetMachineCode();
    ThisPcKeyTextBox.Text = LicenseActivationService.GenerateMachineKey();
  }

  private void GenerateCustomerKey_Click(object sender, RoutedEventArgs e)
  {
    var code = CustomerCodeTextBox.Text?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(code))
    {
      MessageBox.Show(this, "Bitte den PC-Code vom Kunden einfügen.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    CustomerKeyTextBox.Text = LicenseActivationService.GenerateMachineKey(code);
    TryCopy(CustomerKeyTextBox.Text);
  }

  private void CopyThisPcCode_Click(object sender, RoutedEventArgs e) => TryCopy(ThisPcCodeTextBox.Text);

  private void CopyThisPcKey_Click(object sender, RoutedEventArgs e) => TryCopy(ThisPcKeyTextBox.Text);

  private void CopyCustomerKey_Click(object sender, RoutedEventArgs e) => TryCopy(CustomerKeyTextBox.Text);

  private void TryCopy(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return;

    try
    {
      Clipboard.SetText(text);
    }
    catch
    {
      MessageBox.Show(this, "Zwischenablage nicht verfügbar.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
