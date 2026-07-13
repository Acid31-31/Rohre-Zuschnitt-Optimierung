using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace RohreZuschnittOptimierung.Services;

internal static class AdminElevationService
{
  private const int ErrorCancelled = 1223;

  public static bool IsRunningAsAdministrator()
  {
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
  }

  public static bool TryRelaunchAsAdministrator()
  {
    if (IsRunningAsAdministrator())
      return true;

    var exePath = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
      return false;

    try
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = exePath,
        Arguments = BuildRelaunchArguments(),
        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
        Verb = "runas",
        UseShellExecute = true
      });
      return true;
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
    {
      return false;
    }
    catch
    {
      return false;
    }
  }

  private static string BuildRelaunchArguments()
  {
    var args = Environment.GetCommandLineArgs();
    if (args.Length <= 1)
      return string.Empty;

    return string.Join(" ", args.Skip(1).Select(QuoteArgument));
  }

  private static string QuoteArgument(string value)
  {
    if (string.IsNullOrEmpty(value))
      return "\"\"";

    if (!value.Contains(' ') && !value.Contains('"'))
      return value;

    return "\"" + value.Replace("\"", "\\\"") + "\"";
  }
}
