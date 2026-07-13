using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RohreZuschnittOptimierung.Services;

public static class WindowChromeService
{
  private const int DwmwaUseImmersiveDarkMode = 20;
  private const int DwmwaUseImmersiveDarkModeLegacy = 19;

  [DllImport("dwmapi.dll", PreserveSig = true)]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

  public static void ApplyTheme(Window window, bool isDark)
  {
    if (!OperatingSystem.IsWindowsVersionAtLeast(10))
      return;

    var helper = new WindowInteropHelper(window);
    var handle = helper.Handle;
    if (handle == IntPtr.Zero)
      return;

    var useDark = isDark ? 1 : 0;
    if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
      DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref useDark, sizeof(int));
  }

  public static void ApplyThemeToAllWindows(bool isDark)
  {
    if (Application.Current is null)
      return;

    foreach (Window window in Application.Current.Windows)
      ApplyTheme(window, isDark);
  }
}
