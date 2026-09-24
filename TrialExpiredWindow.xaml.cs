using System.Windows;
using System.Windows.Input;
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
      + "\n\nEin Update verlängert die Testlaufzeit auf " + AppInfo.TrialPeriodDays
      + " Tage. Bitte Update prüfen oder die neue Version vom USB-Stick installieren.";
  }

  private async void Update_Click(object sender, RoutedEventArgs e)
  {
    UpdateButton.IsEnabled = false;
    Mouse.OverrideCursor = Cursors.Wait;
    try
    {
      var update = await GitHubUpdateService.CheckForUpdateAsync();
      if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
      {
        MessageBox.Show(this, update.ErrorMessage, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      if (!update.UpdateAvailable)
      {
        MessageBox.Show(
          this,
          "Kein neueres Update gefunden.\n\nBitte die neue Version vom USB-Stick installieren"
          + " (Ordner Rohre-Zuschnitt). Die Testlaufzeit wird danach auf "
          + AppInfo.TrialPeriodDays + " Tage verlängert.",
          "Update-Prüfung",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      var dialog = new UpdateAvailableWindow(update)
      {
        Owner = this,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
      };
      dialog.ShowDialog();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    finally
    {
      Mouse.OverrideCursor = null;
      UpdateButton.IsEnabled = true;
    }
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
