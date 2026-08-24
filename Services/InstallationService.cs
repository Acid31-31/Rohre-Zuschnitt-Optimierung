using System.Diagnostics;
using System.IO;

namespace RohreZuschnittOptimierung.Services;

internal sealed class InstallationResult
{
  public bool Success { get; set; }

  public string InstalledExePath { get; set; } = string.Empty;

  public string Message { get; set; } = string.Empty;
}

internal static class InstallationService
{
  private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
  {
    AppInfo.UsbLauncherFileName,
    AppInfo.UsbUninstallerFileName,
    "STARTEN.bat",
    "DEINSTALLIEREN.bat",
    "README_USB.txt"
  };

  public static string GetSourceDirectory() =>
    ApplicationHostPaths.GetApplicationDirectory();

  /// <summary>
  /// Portable Einrichtung: Programm bleibt im aktuellen Ordner (USB/Desktop).
  /// Erstellt Desktop-Verknuepfung und optional Zertifikat – ohne Program Files.
  /// </summary>
  public static InstallationResult RunInstall(
    string sourceDir,
    bool installPublisherCertificate,
    bool launchAfterInstall,
    IProgress<string>? progress = null)
  {
    var result = new InstallationResult();
    try
    {
      if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        throw new InvalidOperationException("Programmordner nicht gefunden.");

      var appDir = Path.GetFullPath(sourceDir)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

      progress?.Report("Programmdateien werden vorbereitet...");
      var installedExe = EnsureMainExecutable(appDir);

      if (installPublisherCertificate)
      {
        progress?.Report("Herausgeber-Zertifikat wird installiert...");
        CertificateTrustService.TryInstallPublisherCertificate();
      }

      progress?.Report("Desktop-Verknuepfung wird erstellt...");
      DesktopShortcutService.TryCreate(installedExe, out _);

      PortableSetupService.MarkConfigured(appDir);

      result.Success = true;
      result.InstalledExePath = installedExe;
      result.Message = "Einrichtung abgeschlossen.";

      if (launchAfterInstall && File.Exists(installedExe))
      {
        progress?.Report("Anwendung wird gestartet...");
        Process.Start(new ProcessStartInfo
        {
          FileName = installedExe,
          WorkingDirectory = appDir,
          UseShellExecute = true
        });
      }
    }
    catch (Exception ex)
    {
      result.Success = false;
      result.Message = ex.Message;
    }

    return result;
  }

  private static string EnsureMainExecutable(string appDir)
  {
    var targetExe = Path.Combine(appDir, AppInfo.ExeFileName);
    if (File.Exists(targetExe))
      return targetExe;

    foreach (var candidateName in new[] { AppInfo.UsbLauncherFileName, AppInfo.UsbUninstallerFileName })
    {
      var sourceExe = Path.Combine(appDir, candidateName);
      if (!File.Exists(sourceExe))
        continue;

      File.Copy(sourceExe, targetExe, true);
      return targetExe;
    }

    throw new InvalidOperationException("Hauptprogramm nicht im Ordner gefunden.");
  }
}
