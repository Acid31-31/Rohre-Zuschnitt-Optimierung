using System.Globalization;
using System.Windows;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class PdfSettingsWindow : Window
{
  public PdfSettingsWindow()
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
    LocalAiEnabledCheckBox.IsChecked = appSettings.LocalAiEnabled;
    LocalAiStatusTextBlock.Text = BundledAiRuntime.IsBundled
      ? "KI-Paket im Programmordner gefunden (AI\\)."
      : "Hinweis: KI-Paket (AI\\) fehlt – USB-Version R18+ verwenden.";

    var settings = PdfExportSettingsStore.Load();
    ShowCreatedDateCheckBox.IsChecked = settings.ShowCreatedDate;
    ShowSummaryHeaderCheckBox.IsChecked = settings.ShowSummaryHeader;
    ShowTotalSawSummaryCheckBox.IsChecked = settings.ShowTotalSawSummary;
    ShowPartsOverviewCheckBox.IsChecked = settings.ShowPartsOverview;
    ShowBarDiagramCheckBox.IsChecked = settings.ShowBarDiagram;
    ShowBarUsageInfoCheckBox.IsChecked = settings.ShowBarUsageInfo;
    ShowDiagramCutSequenceLineCheckBox.IsChecked = settings.ShowDiagramCutSequenceLine;
    ShowDetailedCutSequenceCheckBox.IsChecked = settings.ShowDetailedCutSequence;
  }

  private PdfExportSettings ReadSettingsFromUi() => new()
  {
    ShowCreatedDate = ShowCreatedDateCheckBox.IsChecked == true,
    ShowSummaryHeader = ShowSummaryHeaderCheckBox.IsChecked == true,
    ShowTotalSawSummary = ShowTotalSawSummaryCheckBox.IsChecked == true,
    ShowPartsOverview = ShowPartsOverviewCheckBox.IsChecked == true,
    ShowBarDiagram = ShowBarDiagramCheckBox.IsChecked == true,
    ShowBarUsageInfo = ShowBarUsageInfoCheckBox.IsChecked == true,
    ShowDiagramCutSequenceLine = ShowDiagramCutSequenceLineCheckBox.IsChecked == true,
    ShowDetailedCutSequence = ShowDetailedCutSequenceCheckBox.IsChecked == true
  };

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
        MessageBox.Show(this, message, "Lokale Vision-KI", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    finally
    {
      ProbeLocalAiButton.IsEnabled = true;
    }
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    if (!double.TryParse(StockLengthMmTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var stockLengthMm)
        || stockLengthMm <= 0)
    {
      MessageBox.Show(this, "Bitte eine gültige Originalstange in mm eingeben (größer als 0).", "Einstellungen",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      StockLengthMmTextBox.Focus();
      return;
    }

    if (!double.TryParse(KerfMmTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var kerfMm)
        || kerfMm < 0)
    {
      MessageBox.Show(this, "Bitte eine gültige Schnittbreite in mm eingeben (0 oder größer).", "Einstellungen",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      KerfMmTextBox.Focus();
      return;
    }

    var existing = AppSettingsStore.Load();
    AppSettingsStore.Save(new AppSettings
    {
      StockLengthMm = stockLengthMm,
      KerfMm = kerfMm,
      LocalAiEnabled = LocalAiEnabledCheckBox.IsChecked == true,
      OllamaBaseUrl = existing.OllamaBaseUrl,
      OllamaVisionModel = existing.OllamaVisionModel
    });
    PdfExportSettingsStore.Save(ReadSettingsFromUi());
    DialogResult = true;
    Close();
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }
}
