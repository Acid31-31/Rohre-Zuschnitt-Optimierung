using System.IO;

using System.Windows;



namespace RohreZuschnittOptimierung.Services;



internal static class UsbInstallService

{

  public static bool IsLicenseKeyToolLaunch(string[]? args = null)
  {
    var processPath = Environment.ProcessPath ?? string.Empty;
    var fileName = Path.GetFileName(processPath);
    if (!fileName.Equals(AppInfo.LicenseKeyToolFileName, StringComparison.OrdinalIgnoreCase)
        && !fileName.Equals("KEY_Rohre_Zuschnitt.exe", StringComparison.OrdinalIgnoreCase))
      return false;

    var folder = Path.GetDirectoryName(processPath) ?? string.Empty;
    var allowPath = Path.Combine(folder, "KEY_Rohre_Zuschitt.allow");
    return File.Exists(allowPath);
  }

  public static bool IsUsbInstallerLaunch(string[]? args = null)

  {

    if (HasUninstallArgument(args))

      return false;



    var fileName = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
    return fileName.Equals(AppInfo.UsbLauncherFileName, StringComparison.OrdinalIgnoreCase);

  }



  public static bool IsUsbUninstallerLaunch(string[]? args = null)

  {

    if (HasUninstallArgument(args))

      return true;



    var fileName = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
    return fileName.Equals(AppInfo.UsbUninstallerFileName, StringComparison.OrdinalIgnoreCase);

  }



  public static bool IsWizardLaunch(string[]? args = null) =>

    IsUsbInstallerLaunch(args) || IsUsbUninstallerLaunch(args);



  /// <summary>Portable: Ausfuehrung von USB, Desktop oder jedem Ordner erlaubt.</summary>

  public static bool EnforceInstalledExecution() => true;



  public static string GetApplicationDirectory() =>

    ApplicationHostPaths.GetApplicationDirectory();



  private static bool HasUninstallArgument(string[]? args)

  {

    if (args is null || args.Length == 0)

      return false;



    return args.Any(static argument =>

      string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase)

      || string.Equals(argument, "/uninstall", StringComparison.OrdinalIgnoreCase));

  }

}


