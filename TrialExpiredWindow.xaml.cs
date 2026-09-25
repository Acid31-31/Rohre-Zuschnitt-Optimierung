using System.Windows;
using System.Windows.Input;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class TrialExpiredWindow : Window
{
  public bool ActivatedFullVersion { get; private set; }

  public TrialExpiredWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    var status = TrialLicenseService.Evaluate();
    var machineCode = LicenseActivationService.GetMachineCode();

    MessageTextBlock.Text =
      "Die " + AppInfo.TrialPeriodDays + "-Tage-Testversion von " + AppInfo.ProductName
      + " ist abgelaufen.";

    DetailTextBlock.Text =
      "Erststart: " + status.FirstRunLocal.ToString("dd.MM.yyyy")
      + "   |   Gültig bis: " + status.ExpiresLocal.ToString("dd.MM.yyyy")
      + "\n\nOption 1: Update installieren (Testlaufzeit 90 Tage in neuer Version)."
      + "\nOption 2: Lizenzschlüssel eingeben und Vollversion freischalten.";

    MachineCodeTextBlock.Text = "PC-Code (an Anbieter senden): " + machineCode;
  }

  private void Activate_Click(object sender, RoutedEventArgs e)
  {
    if (!LicenseActivationService.TryActivate(LicenseKeyTextBox.Text, out var message))
    {
      MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    ActivatedFullVersion = true;
    MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Information);
    DialogResult = true;
    Close();
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
        MessageBox.Show(
          this,
          update.ErrorMessage
          + "\n\nAlternative: Neue Version vom USB-Stick installieren oder Lizenzschlüssel verwenden.",
          "Update-Prüfung",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }

      if (!update.UpdateAvailable)
      {
        MessageBox.Show(
          this,
          "Kein neueres Update gefunden.\n\nBitte die neue Version vom USB-Stick installieren"
          + " oder Lizenzschlüssel freischalten.",
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

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }
}
