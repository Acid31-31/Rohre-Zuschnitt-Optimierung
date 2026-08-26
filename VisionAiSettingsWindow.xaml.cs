using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class VisionAiSettingsWindow : Window
{
  public VisionAiSettingsWindow()
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
    LocalAiEnabledCheckBox.IsChecked = appSettings.LocalAiEnabled;
    LocalAiStatusTextBlock.Text = BundledAiRuntime.IsBundled
      ? "KI-Paket im Programmordner gefunden (AI\\)."
      : "Hinweis: KI-Paket (AI\\) fehlt – USB-Version R18+ verwenden.";
  }

  private async void ProbeLocalAi_Click(object sender, RoutedEventArgs e)
  {
    ProbeLocalAiButton.IsEnabled = false;
    LocalAiStatusTextBlock.Text = "Starte mitgelieferte Vision-KI…";
    try
    {
      var settings = AppSettingsStore.Load();
      settings.LocalAiEnabled = LocalAiEnabledCheckBox.IsChecked == true;
      var (ok, message) = await LocalVisionCutAnalysisService.ProbeAsync(settings).ConfigureAwait(true);
      LocalAiStatusTextBlock.Text = message;
      if (!ok)
        MessageBox.Show(this, message, "Vision-KI", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    finally
    {
      ProbeLocalAiButton.IsEnabled = true;
    }
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    var existing = AppSettingsStore.Load();
    existing.LocalAiEnabled = LocalAiEnabledCheckBox.IsChecked == true;
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
