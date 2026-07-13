using System.IO;
using System.Reflection;

namespace RohreZuschnittOptimierung;

internal static class AppInfo
{
  public const string ProductName = "Rohre Zuschnitt Optimierung";
  public const string ExeFileName = "RohreZuschnittOptimierung.exe";
  public const string CodeSigningCerFileName = "CodeSigning.cer";

  public const string GitHubOwner = "Acid31-31";
  public const string GitHubRepo = "Rohre-Zuschnitt-Optimierung";
  public const string UpdateAssetFileName = "RohreZuschnittOptimierung-Release.zip";

  /// <summary>Repository ist öffentlich – Updates ohne github.token.</summary>
  public const bool GitHubUpdatesArePublic = true;

#if DEBUG
  public const bool RequireCodeSignature = false;
#else
  public const bool RequireCodeSignature = false;
#endif

  public static string DisplayVersion
  {
    get
    {
      var version = ApplicationVersion;
      return $"{version.Major}.{version.Minor}.{version.Build}";
    }
  }

  public static Version ApplicationVersion
  {
    get
    {
      var version = Assembly.GetExecutingAssembly().GetName().Version;
      if (version is null)
        return new Version(1, 0, 1);

      var build = version.Revision > 0 ? version.Revision : version.Build;
      if (build <= 0 && version.Build > 0)
        build = version.Build;

      return new Version(version.Major, version.Minor, Math.Max(build, 0));
    }
  }

  public static string GitHubLatestReleaseApiUrl =>
    $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

  public static string UserDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Rohre-Zuschnitt-Optimierung");

  public static string DefaultInstallDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    "Rohre-Zuschnitt-Optimierung");

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
