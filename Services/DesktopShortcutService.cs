using System.IO;
using System.Reflection;

namespace RohreZuschnittOptimierung.Services;

internal static class DesktopShortcutService
{
  public static bool TryCreate(string targetExePath, out string message)
  {
    message = string.Empty;
    try
    {
      if (string.IsNullOrWhiteSpace(targetExePath) || !File.Exists(targetExePath))
      {
        message = "Programmdatei nicht gefunden.";
        return false;
      }

      var desktopPath = ResolveDesktopPath();
      if (string.IsNullOrWhiteSpace(desktopPath))
      {
        message = "Desktop-Ordner nicht gefunden.";
        return false;
      }

      var baseDir = Path.GetDirectoryName(targetExePath);
      var shortcutPath = Path.Combine(desktopPath, AppInfo.ShortcutFileName);

      var wshShellType = Type.GetTypeFromProgID("WScript.Shell");
      if (wshShellType is null)
      {
        message = "Windows-Shell nicht verfuegbar.";
        return false;
      }

      dynamic wshShell = Activator.CreateInstance(wshShellType)!;
      dynamic shortcut = wshShell.CreateShortcut(shortcutPath);
      shortcut.TargetPath = targetExePath;
      shortcut.WorkingDirectory = baseDir;
      shortcut.IconLocation = targetExePath + ",0";
      shortcut.Description = AppInfo.ProductName + " starten";
      shortcut.Save();

      if (!File.Exists(shortcutPath))
      {
        message = "Verknuepfung konnte nicht gespeichert werden.";
        return false;
      }

      message = shortcutPath;
      return true;
    }
    catch (Exception ex)
    {
      message = ex.Message;
      return false;
    }
  }

  /// <summary>
  /// Erneuert die Desktop-Verknüpfung, wenn sie fehlt oder auf eine verschwundene EXE zeigt.
  /// </summary>
  public static bool TryRepairToCurrentExe(out string message)
  {
    message = string.Empty;
    try
    {
      var exePath = AppInfo.GetInstalledExePath();
      if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
      {
        message = "Aktuelle Programmdatei nicht gefunden.";
        return false;
      }

      var desktopPath = ResolveDesktopPath();
      if (string.IsNullOrWhiteSpace(desktopPath))
      {
        message = "Desktop-Ordner nicht gefunden.";
        return false;
      }

      var shortcutPath = Path.Combine(desktopPath, AppInfo.ShortcutFileName);
      if (File.Exists(shortcutPath) && TryReadTargetPath(shortcutPath, out var currentTarget)
          && !string.IsNullOrWhiteSpace(currentTarget)
          && File.Exists(currentTarget)
          && string.Equals(Path.GetFullPath(currentTarget), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
      {
        message = "Verknuepfung ist aktuell.";
        return true;
      }

      return TryCreate(exePath, out message);
    }
    catch (Exception ex)
    {
      message = ex.Message;
      return false;
    }
  }

  public static bool TryReadTargetPath(string shortcutPath, out string targetPath)
  {
    targetPath = string.Empty;
    try
    {
      var wshShellType = Type.GetTypeFromProgID("WScript.Shell");
      if (wshShellType is null)
        return false;

      dynamic wshShell = Activator.CreateInstance(wshShellType)!;
      dynamic shortcut = wshShell.CreateShortcut(shortcutPath);
      targetPath = (string?)shortcut.TargetPath ?? string.Empty;
      return !string.IsNullOrWhiteSpace(targetPath);
    }
    catch
    {
      return false;
    }
  }

  public static string? ResolveDesktopPath()
  {
    var candidates = new[]
    {
      Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
      Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
    };

    foreach (var candidate in candidates)
    {
      if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
        return candidate;
    }

    return null;
  }
}
