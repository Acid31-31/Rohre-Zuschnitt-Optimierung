using System.Diagnostics;
using System.IO;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class UpdateApplyRunner
{
  public static bool TryParseApplyUpdateArgs(string[] args, out string stagedRoot, out string targetRoot, out int parentProcessId)
  {
    stagedRoot = string.Empty;
    targetRoot = string.Empty;
    parentProcessId = 0;

    if (args.Length < 3)
      return false;

    if (!string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
      return false;

    stagedRoot = args[1];
    targetRoot = args[2];
    if (string.IsNullOrWhiteSpace(stagedRoot) || string.IsNullOrWhiteSpace(targetRoot))
      return false;

    if (args.Length >= 4)
      int.TryParse(args[3], out parentProcessId);

    stagedRoot = Path.GetFullPath(stagedRoot);
    targetRoot = Path.GetFullPath(targetRoot);
    return Directory.Exists(stagedRoot);
  }

  public static void ApplyUpdate(
    string stagedRoot,
    string targetRoot,
    IProgress<UpdateProgressInfo>? progress = null,
    CancellationToken cancellationToken = default,
    int parentProcessId = 0)
  {
    stagedRoot = Path.GetFullPath(stagedRoot);
    targetRoot = Path.GetFullPath(targetRoot);

    if (!Directory.Exists(stagedRoot))
      throw new DirectoryNotFoundException("Update-Paket nicht gefunden: " + stagedRoot);

    if (AppInfo.IsProtectedInstallDirectory(targetRoot) && !AdminElevationService.IsRunningAsAdministrator())
    {
      AdminElevationService.TryRelaunchAsAdministrator();
      return;
    }

    Report(progress, 2, "Warte auf Beendigung der Anwendung…");
    WaitForTargetUnlock(targetRoot, progress, cancellationToken, parentProcessId);

    string? backupRoot = null;
    try
    {
      Report(progress, 8, "Sicherungskopie wird erstellt…");
      backupRoot = AppSecurityService.CreateInstallBackup(targetRoot);

      Report(progress, 12, "Dateien werden installiert…");
      CopyApplicationFiles(stagedRoot, targetRoot, progress, cancellationToken);

      Report(progress, 94, "Installation wird geprüft…");
      if (!AppSecurityService.TryVerifyApplicationPackage(targetRoot, out var verifyMessage, targetRoot))
        throw new InvalidOperationException("Update-Installation ungültig: " + verifyMessage);
    }
    catch
    {
      if (!string.IsNullOrWhiteSpace(backupRoot))
      {
        try { AppSecurityService.RestoreInstallBackup(backupRoot, targetRoot); }
        catch { /* ignore */ }
      }

      throw;
    }
    finally
    {
      AppSecurityService.DeleteDirectorySafe(backupRoot);
    }

    Report(progress, 98, "Anwendung wird gestartet…");
    var exePath = Path.Combine(targetRoot, AppInfo.ExeFileName);
    if (File.Exists(exePath))
    {
      DesktopShortcutService.TryCreate(exePath, out _);

      Process.Start(new ProcessStartInfo
      {
        FileName = exePath,
        WorkingDirectory = targetRoot,
        UseShellExecute = true
      });
    }

    Report(progress, 100, "Update abgeschlossen.");
  }

  private static void WaitForTargetUnlock(
    string targetRoot,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken,
    int parentProcessId)
  {
    if (parentProcessId > 0)
      WaitForProcessExit(parentProcessId, progress, cancellationToken);

    var lockFile = Path.GetFullPath(Path.Combine(targetRoot, AppInfo.ExeFileName));
    for (var attempt = 0; attempt < 120; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!IsExecutableLocked(lockFile))
      {
        Report(progress, 10, "Anwendung beendet, Installation läuft…");
        return;
      }

      Report(progress, Math.Min(10, 2 + attempt / 4), "Warte auf Beendigung der Anwendung…");
      Thread.Sleep(250);
    }

    throw new InvalidOperationException(
      "Die alte Anwendung konnte nicht beendet werden. Bitte alle Fenster schließen und erneut versuchen.");
  }

  private static void WaitForProcessExit(int processId, IProgress<UpdateProgressInfo>? progress, CancellationToken cancellationToken)
  {
    for (var attempt = 0; attempt < 120; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        using var process = Process.GetProcessById(processId);
        if (process.HasExited)
          return;
      }
      catch (ArgumentException)
      {
        return;
      }

      Report(progress, Math.Min(9, 2 + attempt / 6), "Warte auf Beendigung der Anwendung…");
      Thread.Sleep(250);
    }
  }

  private static bool IsExecutableLocked(string executablePath)
  {
    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppInfo.ExeFileName)))
    {
      try
      {
        if (process.Id == Environment.ProcessId)
          continue;

        var modulePath = process.MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(modulePath)
            && string.Equals(Path.GetFullPath(modulePath), executablePath, StringComparison.OrdinalIgnoreCase))
          return true;
      }
      catch { /* ignore */ }
      finally
      {
        process.Dispose();
      }
    }

    if (!File.Exists(executablePath))
      return false;

    try
    {
      using (File.Open(executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        return false;
    }
    catch (IOException) { return true; }
    catch (UnauthorizedAccessException) { return true; }
  }

  private static void CopyApplicationFiles(
    string sourceRoot,
    string targetRoot,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(targetRoot);
    var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
      .Where(file => IsApplicationFileExtension(Path.GetExtension(file)))
      .ToArray();

    var total = Math.Max(1, files.Length);
    for (var index = 0; index < files.Length; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var file = files[index];
      var relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var destination = Path.Combine(targetRoot, relative);
      var destinationDir = Path.GetDirectoryName(destination);
      if (!string.IsNullOrWhiteSpace(destinationDir))
        Directory.CreateDirectory(destinationDir);

      File.Copy(file, destination, true);
      Report(progress, 12 + (int)((index + 1) * 84.0 / total), "Installiere: " + Path.GetFileName(file));
    }
  }

  private static void Report(IProgress<UpdateProgressInfo>? progress, int percent, string message) =>
    progress?.Report(new UpdateProgressInfo(percent, message));

  private static bool IsApplicationFileExtension(string extension) =>
    extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
}
