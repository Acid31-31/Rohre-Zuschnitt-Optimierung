using System.IO;

namespace RohreZuschnittOptimierung.Services;

internal static class PortableDataMigrationService
{
  public static void TryMigrateLegacyUserData()
  {
    var targetRoot = AppInfo.UserDataDirectory;
    var legacyRoot = AppInfo.LegacyUserDataDirectory;

    if (string.Equals(
          Path.GetFullPath(targetRoot),
          Path.GetFullPath(legacyRoot),
          StringComparison.OrdinalIgnoreCase))
      return;

    if (!Directory.Exists(legacyRoot))
      return;

    Directory.CreateDirectory(targetRoot);

    foreach (var file in Directory.GetFiles(legacyRoot))
    {
      var name = Path.GetFileName(file);
      var destination = Path.Combine(targetRoot, name);
      if (File.Exists(destination))
        continue;

      try
      {
        File.Copy(file, destination, false);
      }
      catch
      {
      }
    }
  }
}
