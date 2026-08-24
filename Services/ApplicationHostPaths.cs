using System.IO;
using System.Reflection;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// .NET 8: Assembly.Location zeigt auf die DLL, nicht auf die startende EXE.
/// </summary>
internal static class ApplicationHostPaths
{
  public static string? GetHostExecutablePath()
  {
    var processPath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
      return processPath;

    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    foreach (var candidate in new[]
             {
               AppInfo.UsbLauncherFileName,
               AppInfo.ExeFileName,
               AppInfo.UsbUninstallerFileName
             })
    {
      var path = Path.Combine(baseDir, candidate);
      if (File.Exists(path))
        return path;
    }

    return null;
  }

  public static string GetHostExecutableFileName()
  {
    var hostPath = GetHostExecutablePath();
    return string.IsNullOrWhiteSpace(hostPath)
      ? string.Empty
      : Path.GetFileName(hostPath);
  }

  public static string GetApplicationDirectory()
  {
    var hostPath = GetHostExecutablePath();
    if (!string.IsNullOrWhiteSpace(hostPath))
    {
      return Path.GetDirectoryName(hostPath)!
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    return AppDomain.CurrentDomain.BaseDirectory
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
  }
}
