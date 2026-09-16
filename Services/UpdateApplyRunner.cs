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
    Report(progress, 14, "Dateien werden installiert…");

    var arguments =
      $"\"{sourceRoot}\" \"{targetRoot}\" /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XF *.pdb /XD Daten AI";
    var startInfo = new ProcessStartInfo
    {
      FileName = "robocopy",
      Arguments = arguments,
      CreateNoWindow = true,
      UseShellExecute = false
    };

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("robocopy konnte nicht gestartet werden.");

    while (!process.WaitForExit(500))
    {
      cancellationToken.ThrowIfCancellationRequested();
      Report(progress, 50, "Dateien werden installiert…");
    }

    if (process.ExitCode >= 8)
      throw new InvalidOperationException("Dateien konnten nicht installiert werden (robocopy " + process.ExitCode + ").");

    Report(progress, 90, "Installation fast abgeschlossen…");
  }

  private static void Report(IProgress<UpdateProgressInfo>? progress, int percent, string message) =>
    progress?.Report(new UpdateProgressInfo(percent, message));
}
