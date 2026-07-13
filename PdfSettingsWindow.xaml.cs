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

  private void Save_Click(object sender, RoutedEventArgs e)
  {
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
