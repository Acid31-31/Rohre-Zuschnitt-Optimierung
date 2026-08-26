using System.Globalization;
using System.Windows;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class GeneralSettingsWindow : Window
{
  public GeneralSettingsWindow()
  {
    InitializeComponent();
    Loaded += (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      LoadSettings();
    };
  }

  private void LoadSettings()
  {
    var appSettings = AppSettingsStore.Load();
    StockLengthMmTextBox.Text = appSettings.StockLengthMm.ToString("0.###", CultureInfo.InvariantCulture);
    KerfMmTextBox.Text = appSettings.KerfMm.ToString("0.###", CultureInfo.InvariantCulture);
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    if (!double.TryParse(StockLengthMmTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var stockLengthMm)
        || stockLengthMm <= 0)
    {
      MessageBox.Show(this, "Bitte eine gültige Originalstange in mm eingeben (größer als 0).", "Optimierung",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      StockLengthMmTextBox.Focus();
      return;
    }

    if (!double.TryParse(KerfMmTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var kerfMm)
        || kerfMm < 0)
    {
      MessageBox.Show(this, "Bitte eine gültige Schnittbreite in mm eingeben (0 oder größer).", "Optimierung",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      KerfMmTextBox.Focus();
      return;
    }

    var existing = AppSettingsStore.Load();
    existing.StockLengthMm = stockLengthMm;
    existing.KerfMm = kerfMm;
    AppSettingsStore.Save(existing);
    DialogResult = true;
    Close();
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }
}
