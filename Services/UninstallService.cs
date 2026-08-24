using System.IO;

namespace RohreZuschnittOptimierung.Services;

internal sealed class UninstallResult
{
  public bool Success { get; set; }

  public string Message { get; set; } = string.Empty;
}

internal static class UninstallService
{
  public static bool IsInstalled()
  {
    if (File.Exists(Path.Combine(AppInfo.LegacyProgramFilesDirectory, AppInfo.ExeFileName)))
      return true;

    if (HasDesktopShortcut())
      return true;

    return PortableSetupService.IsConfigured(ApplicationHostPaths.GetApplicationDirectory());
  }

  public static UninstallResult RunUninstall(bool removeUserData, IProgress<string>? progress = null)
  {
    var result = new UninstallResult();
    try
    {
      var legacyDir = AppInfo.LegacyProgramFilesDirectory;
      var needsAdmin = Directory.Exists(legacyDir);

      if (needsAdmin && !AdminElevationService.IsRunningAsAdministrator())
        throw new InvalidOperationException("Administratorrechte erforderlich (alte Program-Files-Installation).");

      progress?.Report("Desktop-Verknuepfung wird entfernt...");
      RemoveDesktopShortcut();

      if (Directory.Exists(legacyDir))
      {
        progress?.Report("Alte Program-Files-Installation wird entfernt...");
        Directory.Delete(legacyDir, true);
      }

      PortableSetupService.Clear(ApplicationHostPaths.GetApplicationDirectory());

      if (removeUserData)
      {
        progress?.Report("Benutzerdaten werden entfernt...");
        RemoveUserData();
      }

      InstallSessionStore.Clear();
      UninstallSessionStore.Clear();
      result.Success = true;
      result.Message = removeUserData
        ? AppInfo.ProductName + " wurde vollstaendig entfernt. Der Programmordner auf USB/Desktop bleibt erhalten."
        : AppInfo.ProductName + " wurde entfernt (Verknuepfung/Einrichtung). Programmordner und Benutzerdaten bleiben erhalten.";
    }
    catch (Exception ex)
    {
      result.Success = false;
      result.Message = ex.Message;
    }

    return result;
  }

  private static bool HasDesktopShortcut()
  {
    var desktop = DesktopShortcutService.ResolveDesktopPath();
    if (string.IsNullOrWhiteSpace(desktop))
      return false;

    return File.Exists(Path.Combine(desktop, AppInfo.ShortcutFileName));
  }

  private static void RemoveDesktopShortcut()
  {
    var desktop = DesktopShortcutService.ResolveDesktopPath();
    if (string.IsNullOrWhiteSpace(desktop))
      return;

    var shortcut = Path.Combine(desktop, AppInfo.ShortcutFileName);
    if (File.Exists(shortcut))
      File.Delete(shortcut);
  }

  private static void RemoveUserData()
  {
    var userData = AppInfo.UserDataDirectory;
    if (!Directory.Exists(userData))
      return;

    Directory.Delete(userData, true);
  }
}
