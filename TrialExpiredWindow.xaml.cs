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
    Loaded += (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      Dispatcher.BeginInvoke(() =>
      {
        KeyToolChiptunePlayer.Start();
        RefreshMusicButton();
      });
    };
    Closed += (_, _) => KeyToolChiptunePlayer.Stop();

    var status = TrialLicenseService.Evaluate();
    var machineCode = LicenseActivationService.GetMachineCode();

    MessageTextBlock.Text =
      "Die " + AppInfo.TrialPeriodDays + "-Tage-Testversion von " + AppInfo.ProductName
      + " ist abgelaufen.";

    DetailTextBlock.Text =
      "Erststart: " + status.FirstRunLocal.ToString("dd.MM.yyyy")
      + "   |   Gültig bis: " + status.ExpiresLocal.ToString("dd.MM.yyyy")
      + "\n\nEinzel-Lizenz: PC-Code kopieren und an den Anbieter senden."
      + " Der Schlüssel gilt nur für diesen Rechner. Weitere PCs: dort den eigenen Code schicken.";

    PcCodeTextBox.Text = machineCode;
  }

  private void MusicToggle_Click(object sender, RoutedEventArgs e)
  {
    KeyToolChiptunePlayer.Toggle();
    RefreshMusicButton();
  }

  private void RefreshMusicButton()
  {
    MusicToggleButton.Content = KeyToolChiptunePlayer.IsPlaying ? "Musik aus" : "Musik an";
  }

  private void CopyPcCode_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      Clipboard.SetText(PcCodeTextBox.Text);
    }
    catch
    {
      MessageBox.Show(this, "Zwischenablage nicht verfügbar.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
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
