using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    ThemeService.Initialize(this);

    DispatcherUnhandledException += (_, args) =>
    {
      try
      {
        MessageBox.Show(
          "Unerwarteter Fehler:" + Environment.NewLine + args.Exception.Message,
          AppInfo.ProductName,
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
      catch
      {
      }
      args.Handled = true;
    };

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

    if (UsbInstallService.IsLicenseKeyToolLaunch(e.Args))
    {
      new LicenseKeyWindow().ShowDialog();
      Shutdown();
      return;
    }

    PortableDataMigrationService.TryMigrateLegacyUserData();
    PdfFontBootstrap.EnsureInitialized();
    AppSecurityService.Initialize();

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
      var expiredWindow = new TrialExpiredWindow();
      expiredWindow.ShowDialog();
      if (!expiredWindow.ActivatedFullVersion)
      {
        Shutdown();
        return;
      }

      trialStatus = TrialLicenseService.Evaluate();
    }

    if (TrialLicenseService.ShouldShowWelcome())
    {
      var welcome = trialStatus.WasExtended
        ? "Die Testlaufzeit wurde auf " + AppInfo.TrialPeriodDays + " Tage verlängert.\n\n"
          + "Gültig bis " + trialStatus.ExpiresLocal.ToString("dd.MM.yyyy") + "."
        : "Willkommen bei der Testversion von " + AppInfo.ProductName + ".\n\n"
          + "Die Testversion ist " + AppInfo.TrialPeriodDays + " Tage ab dem ersten Start gültig"
          + " (bis " + trialStatus.ExpiresLocal.ToString("dd.MM.yyyy") + ").";
      MessageBox.Show(
        welcome,
        AppInfo.ProductName + " – Testversion",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      TrialLicenseService.MarkWelcomeShown();
    }

    // Fenster zuerst zeigen – Lager/Netzwerk erst danach im Hintergrund.
    var mainWindow = new MainWindow(trialStatus);
    mainWindow.Show();
    base.OnStartup(e);
  }
}