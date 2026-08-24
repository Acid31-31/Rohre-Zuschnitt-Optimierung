using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace RohreZuschnittOptimierung.Services;

internal static class LicenseContentService
{
  public static string LoadLicenseText()
  {
    var fromFile = TryLoadFromFile();
    if (!string.IsNullOrWhiteSpace(fromFile))
      return fromFile;

    return "Copyright (c) " + DateTime.Now.Year + " Alexander Hoelzer\nAlle Rechte vorbehalten.";
  }

  public static string GetCopyrightSummary() =>
    "Copyright (c) 2026 Alexander Hoelzer · Alle Rechte vorbehalten · Proprietaere Software";

  private static string? TryLoadFromFile()
  {
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
    var candidates = new[]
    {
      Path.Combine(baseDir, "LICENSE_DE.txt"),
      Path.Combine(assemblyDir, "LICENSE_DE.txt"),
      Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "LICENSE_DE.txt"))
    };

    foreach (var path in candidates)
    {
      if (!File.Exists(path))
        continue;

      try
      {
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(text))
          return text;
      }
      catch
      {
      }
    }

    return null;
  }
}
