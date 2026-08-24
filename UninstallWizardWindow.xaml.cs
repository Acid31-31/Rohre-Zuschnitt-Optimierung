using System.IO;
using System.Windows;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class UninstallWizardWindow : Window
{
  private int _step;
  private bool _isRunning;
  private readonly bool _isInstalled;

  public UninstallWizardWindow()
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    _isInstalled = UninstallService.IsInstalled();
    WelcomeVersionTextBlock.Text = "Version " + AppInfo.DisplayVersion;
    WelcomeCopyrightTextBlock.Text = LicenseContentService.GetCopyrightSummary();
    FooterCopyrightTextBlock.Text = LicenseContentService.GetCopyrightSummary();
    InformationTextBlock.Text = UninstallContentService.BuildInformationText();
    InstallPathTextBox.Text = AppInfo.GetApplicationDirectory();
    UserDataPathTextBox.Text = AppInfo.UserDataDirectory;
    ShortcutPathTextBox.Text = BuildShortcutPath();

    if (!_isInstalled)
      NotInstalledTextBlock.Visibility = Visibility.Visible;

    Loaded += UninstallWizardWindow_Loaded;
    ShowStep(0);
  }

  private async void UninstallWizardWindow_Loaded(object sender, RoutedEventArgs e)
  {
    if (!UninstallSessionStore.TryConsumePending(out var pending) || pending is null)
      return;

    RemoveUserDataCheckBox.IsChecked = pending.RemoveUserData;
    ConfirmUninstallCheckBox.IsChecked = pending.ConfirmationAccepted;
    AcceptInformationCheckBox.IsChecked = pending.ConfirmationAccepted;

    _isRunning = true;
    ShowStep(3);
    await RunUninstallAsync(pending.RemoveUserData).ConfigureAwait(true);
  }

  private static string BuildShortcutPath()
  {
    var desktop = DesktopShortcutService.ResolveDesktopPath();
    return string.IsNullOrWhiteSpace(desktop)
      ? AppInfo.ShortcutFileName
      : Path.Combine(desktop, AppInfo.ShortcutFileName);
  }

  private void ShowStep(int step)
  {
    _step = step;
    WelcomePanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
    InformationPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
    OptionsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
    ProgressPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
    FinishPanel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

    BackButton.IsEnabled = step > 0 && step < 3 && !_isRunning;
    NextButton.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
    NextButton.IsEnabled = _isInstalled || step == 0;
    UninstallButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
    UninstallButton.IsEnabled = _isInstalled;
    CloseButton.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
    CancelButton.IsEnabled = !_isRunning && step < 4;

    StepTitleTextBlock.Text = step switch
    {
      0 => "Schritt 1 von 4 – Willkommen",
      1 => "Schritt 2 von 4 – Informationen",
      2 => "Schritt 3 von 4 – Deinstallationsoptionen",
      3 => "Schritt 4 von 4 – Deinstallation",
      _ => "Abgeschlossen"
    };
  }

  private void BackButton_Click(object sender, RoutedEventArgs e)
  {
    if (_step > 0)
      ShowStep(_step - 1);
  }

  private void NextButton_Click(object sender, RoutedEventArgs e)
  {
    if (!_isInstalled)
    {
      MessageBox.Show(this, "Es wurde noch keine Einrichtung gefunden.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (_step == 1 && AcceptInformationCheckBox.IsChecked != true)
    {
      MessageBox.Show(this, "Bitte bestaetigen Sie, dass Sie die Informationen gelesen haben.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    if (_step < 2)
      ShowStep(_step + 1);
  }

  private async void UninstallButton_Click(object sender, RoutedEventArgs e)
  {
    if (_isRunning)
      return;

    if (!_isInstalled)
    {
      MessageBox.Show(this, "Es wurde noch keine Einrichtung gefunden.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (AcceptInformationCheckBox.IsChecked != true)
    {
      MessageBox.Show(this, "Bitte lesen Sie zuerst die Informationen auf Schritt 2.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      ShowStep(1);
      return;
    }

    if (ConfirmUninstallCheckBox.IsChecked != true)
    {
      MessageBox.Show(this, "Bitte bestaetigen Sie die Deinstallation.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    var removeUserData = RemoveUserDataCheckBox.IsChecked == true;
    var needsAdmin = Directory.Exists(AppInfo.LegacyProgramFilesDirectory);
    if (needsAdmin && !AdminElevationService.IsRunningAsAdministrator())
    {
      UninstallSessionStore.SavePending(new UninstallSessionOptions
      {
        ConfirmationAccepted = true,
        RemoveUserData = removeUserData
      });

      if (!AdminElevationService.TryRelaunchAsAdministrator())
      {
        MessageBox.Show(this, "Administratorfreigabe abgebrochen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      Close();
      return;
    }

    _isRunning = true;
    ShowStep(3);
    await RunUninstallAsync(removeUserData).ConfigureAwait(true);
  }

  private async Task RunUninstallAsync(bool removeUserData)
  {
    var progress = new Progress<string>(message => ProgressStatusTextBlock.Text = message);
    var result = await Task.Run(() => UninstallService.RunUninstall(removeUserData, progress)).ConfigureAwait(true);

    _isRunning = false;
    if (!result.Success)
    {
      MessageBox.Show(this, result.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
      ShowStep(2);
      return;
    }

    FinishMessageTextBlock.Text = result.Message
      + Environment.NewLine + Environment.NewLine
      + "Sie koennen das Programm weiterhin ueber den Ordner auf USB/Desktop starten.";

    ShowStep(4);
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e)
  {
    if (_isRunning)
      return;

    DialogResult = false;
    Close();
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = true;
    Close();
  }
}
