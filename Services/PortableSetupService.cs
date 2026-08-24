using System.IO;

namespace RohreZuschnittOptimierung.Services;

internal static class PortableSetupService
{
  private const string MarkerFileName = "portable-setup.json";

  public static string GetMarkerPath(string appDirectory) =>
    Path.Combine(appDirectory, MarkerFileName);

  public static bool IsConfigured(string appDirectory) =>
    File.Exists(GetMarkerPath(appDirectory));

  public static void MarkConfigured(string appDirectory)
  {
    Directory.CreateDirectory(appDirectory);
    File.WriteAllText(
      GetMarkerPath(appDirectory),
      "{\"configuredUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
  }

  public static void Clear(string appDirectory)
  {
    try
    {
      var path = GetMarkerPath(appDirectory);
      if (File.Exists(path))
        File.Delete(path);
    }
    catch
    {
    }
  }
}
