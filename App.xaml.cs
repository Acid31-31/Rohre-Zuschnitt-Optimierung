using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    if (UpdateApplyRunner.TryParseApplyUpdateArgs(
          e.Args ?? [],
          out var stagedRoot,
          out var targetRoot,
          out var parentProcessId))
    {
      var applyWindow = new UpdateApplyProgressWindow(stagedRoot, targetRoot, parentProcessId);
      applyWindow.ShowDialog();
      Shutdown();
      return;
    }

    AppSecurityService.Initialize();
    PortableDataMigrationService.TryMigrateLegacyUserData();
    PdfFontBootstrap.EnsureInitialized();
    ThemeService.Initialize(this);

    if (UsbInstallService.IsUsbUninstallerLaunch(e.Args))
    {
      new UninstallWizardWindow().ShowDialog();
      Shutdown();
      return;
    }

    if (UsbInstallService.IsUsbInstallerLaunch(e.Args))
    {
      new InstallWizardWindow().ShowDialog();
      Shutdown();
      return;
    }

    if (!UsbInstallService.EnforceInstalledExecution())
    {
      Shutdown();
      return;
    }

    var trialStatus = TrialLicenseService.Evaluate();
    if (trialStatus.IsExpired)
    {
      new TrialExpiredWindow().ShowDialog();
      Shutdown();
      return;
    }

    if (TrialLicenseService.ShouldShowWelcome())
    {
      MessageBox.Show(
        "Willkommen bei der Testversion von " + AppInfo.ProductName + ".\n\n"
        + "Die Testversion ist " + AppInfo.TrialPeriodDays + " Tage ab dem ersten Start gültig"
        + " (bis " + trialStatus.ExpiresLocal.ToString("dd.MM.yyyy") + ").",
        AppInfo.ProductName + " – Testversion",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      TrialLicenseService.MarkWelcomeShown();
    }

    PipeWarehouseStore.EnsureInitialized();
    var mainWindow = new MainWindow(trialStatus);
    mainWindow.Show();
    base.OnStartup(e);
  }
}
