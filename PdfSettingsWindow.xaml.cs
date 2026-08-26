using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class PdfSettingsWindow : Window
{
  private bool _anySaved;

  public PdfSettingsWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
  }

  private void OpenGeneral_Click(object sender, RoutedEventArgs e)
    => OpenCategory(new GeneralSettingsWindow { Owner = this });

  private void OpenNetwork_Click(object sender, RoutedEventArgs e)
    => OpenCategory(new NetworkSettingsWindow { Owner = this });

  private void OpenPdf_Click(object sender, RoutedEventArgs e)
    => OpenCategory(new PdfPlanSettingsWindow { Owner = this });

  private void OpenVisionAi_Click(object sender, RoutedEventArgs e)
    => OpenCategory(new VisionAiSettingsWindow { Owner = this });

  private void OpenCategory(Window window)
  {
    if (window.ShowDialog() == true)
      _anySaved = true;
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = _anySaved;
    Close();
  }

  protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
  {
    if (DialogResult is null)
      DialogResult = _anySaved;
    base.OnClosing(e);
  }
}
