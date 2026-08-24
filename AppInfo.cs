using System.IO;
using System.Reflection;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

internal static class AppInfo
{
  public const string ProductName = "Rohre Zuschnitt Optimierung";
  public const string ExeFileName = "RohreZuschnittOptimierung.exe";
  public const string UsbLauncherFileName = "Programm installieren.exe";
  public const string UsbUninstallerFileName = "Programm deinstallieren.exe";
  public const string ShortcutFileName = "Rohre Zuschnitt Optimierung.lnk";
  public const string InstallManifestFileName = "install.manifest";
  public const string CodeSigningCerFileName = "CodeSigning.cer";

  public const string GitHubOwner = "Acid31-31";
  public const string GitHubRepo = "Rohre-Zuschnitt-Optimierung";
  public const string UpdateAssetBaseName = "RohreZuschnittOptimierung-Release";

  /// <summary>Repository ist öffentlich – Updates ohne github.token.</summary>
  public const bool GitHubUpdatesArePublic = true;

#if ENTERPRISE
  public const bool IsTrialEdition = false;
#elif DEBUG
  /// <summary>Debug-Builds ohne Testbeschränkung (Entwicklung).</summary>
  public const bool IsTrialEdition = false;
#else
  public const bool IsTrialEdition = true;
#endif

  public const int TrialPeriodDays = 30;

  public static string EditionLabel => IsTrialEdition ? "Testversion (30 Tage)" : "Vollversion";

#if DEBUG
  public const bool RequireCodeSignature = false;
#else
  public const bool RequireCodeSignature = false;
#endif

  public static string RevisionLabel
  {
    get
    {
      var revision = GetAssemblyRevision();
      return "R" + revision;
    }
  }

  public static string VersionLabel
  {
    get
    {
      var version = ApplicationVersion;
      return $"{version.Major}.{version.Minor}";
    }
  }

  public static string DisplayVersion => VersionLabel + " " + RevisionLabel;

  public static string UpdateAssetFileName => UpdateAssetBaseName + "-" + RevisionLabel + ".zip";

  public static Version ApplicationVersion
  {
    get
    {
      var version = Assembly.GetExecutingAssembly().GetName().Version;
      if (version is null)
        return new Version(1, 0, 1);

      var revision = GetAssemblyRevision();
      return new Version(version.Major, version.Minor, Math.Max(revision, 0));
    }
  }

  private static int GetAssemblyRevision()
  {
    var version = Assembly.GetExecutingAssembly().GetName().Version;
    if (version is null)
      return 1;

    if (version.Revision > 0)
      return version.Revision;

    if (version.Build > 0)
      return version.Build;

    return 0;
  }

  public static string GitHubLatestReleaseApiUrl =>
    $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

  public const string UserDataFolderName = "Daten";

  /// <summary>Portabel: alle Arbeitsdaten im Programmordner (USB/Desktop), nicht in AppData.</summary>
  public static string UserDataDirectory => Path.Combine(
    GetApplicationDirectory(),
    UserDataFolderName);

  public static string LegacyUserDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Rohre-Zuschnitt-Optimierung");

  /// <summary>Fruehere Festinstallation – nur noch fuer Deinstallation alter Versionen.</summary>
  public static string LegacyProgramFilesDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    "Rohre-Zuschnitt-Optimierung");

  public static string GetApplicationDirectory() =>
    ApplicationHostPaths.GetApplicationDirectory();

  public static string GetInstalledExePath()
  {
    var hostExe = ApplicationHostPaths.GetHostExecutablePath();
    if (!string.IsNullOrWhiteSpace(hostExe) && File.Exists(hostExe))
      return hostExe;

    return Path.Combine(LegacyProgramFilesDirectory, ExeFileName);
  }

  public static bool IsProtectedInstallDirectory(string directory)
  {
    if (string.IsNullOrWhiteSpace(directory))
      return false;

    var fullPath = Path.GetFullPath(directory)
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    foreach (var root in GetProtectedInstallRoots())
    {
      if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
          || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  private static string[] GetProtectedInstallRoots() =>
  [
    Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
    Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
  ];
}
