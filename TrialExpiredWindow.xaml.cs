using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class TrialExpiredWindow : Window
{
  public TrialExpiredWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    var status = TrialLicenseService.Evaluate();
    MessageTextBlock.Text =
      "Die " + AppInfo.TrialPeriodDays + "-Tage-Testversion von " + AppInfo.ProductName
      + " ist abgelaufen.";

    DetailTextBlock.Text =
      "Erststart: " + status.FirstRunLocal.ToString("dd.MM.yyyy")
      + "   |   Gültig bis: " + status.ExpiresLocal.ToString("dd.MM.yyyy")
      + "\n\nBitte wenden Sie sich an den Anbieter für eine Vollversion.";
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
