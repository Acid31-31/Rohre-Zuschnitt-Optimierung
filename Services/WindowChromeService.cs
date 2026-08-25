using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RohreZuschnittOptimierung.Services;

public static class WindowChromeService
{
  private const int DwmwaUseImmersiveDarkMode = 20;
  private const int DwmwaUseImmersiveDarkModeLegacy = 19;
  private const int MonitorDefaultToNearest = 2;

  private static readonly Dictionary<Window, Rect> RestoreBounds = new();
  private static readonly HashSet<Window> WorkAreaMaximized = new();
  private static readonly HashSet<Window> BorderlessAttached = new();

  [DllImport("dwmapi.dll", PreserveSig = true)]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

  [DllImport("user32.dll")]
  private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

  [DllImport("user32.dll", CharSet = CharSet.Auto)]
  private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeRect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  private struct MonitorInfo
  {
    public int cbSize;
    public NativeRect rcMonitor;
    public NativeRect rcWork;
    public int dwFlags;
  }

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

  /// <summary>Rahmenloses Fenster wie DOK-V01: kein Windows-Titelbalken, Maximize auf Arbeitsbereich.</summary>
  public static void AttachBorderlessMainWindow(Window window)
  {
    if (window is null || !BorderlessAttached.Add(window))
      return;

    window.WindowStyle = WindowStyle.None;
    window.ResizeMode = ResizeMode.CanResize;
    window.StateChanged += OnBorderlessWindowStateChanged;
    window.Closed += (_, _) =>
    {
      RestoreBounds.Remove(window);
      WorkAreaMaximized.Remove(window);
      BorderlessAttached.Remove(window);
    };

    if (window.IsLoaded)
      MaximizeToWorkArea(window);
    else
      window.Loaded += (_, _) => MaximizeToWorkArea(window);
  }

  public static bool IsWorkAreaMaximized(Window window) =>
    window is not null && WorkAreaMaximized.Contains(window);

  public static void ToggleWorkAreaMaximize(Window window)
  {
    if (window is null)
      return;

    if (IsWorkAreaMaximized(window))
      RestoreFromWorkArea(window);
    else
      MaximizeToWorkArea(window);
  }

  public static void MaximizeToWorkArea(Window window, bool saveRestoreBounds = true)
  {
    if (window is null)
      return;

    if (saveRestoreBounds && !IsWorkAreaMaximized(window))
      RestoreBounds[window] = new Rect(window.Left, window.Top, window.Width, window.Height);

    var work = GetWorkAreaForWindow(window);
    window.WindowState = WindowState.Normal;
    window.Left = work.Left;
    window.Top = work.Top;
    window.Width = work.Width;
    window.Height = work.Height;
    WorkAreaMaximized.Add(window);
  }

  public static void RestoreFromWorkArea(Window window)
  {
    if (window is null)
      return;

    window.WindowState = WindowState.Normal;
    if (RestoreBounds.TryGetValue(window, out var bounds) && bounds.Width > 0 && bounds.Height > 0)
    {
      window.Left = bounds.Left;
      window.Top = bounds.Top;
      window.Width = bounds.Width;
      window.Height = bounds.Height;
    }
    else
    {
      window.Width = 1180;
      window.Height = 760;
      var work = GetWorkAreaForWindow(window);
      window.Left = work.Left + Math.Max(0, (work.Width - window.Width) / 2);
      window.Top = work.Top + Math.Max(0, (work.Height - window.Height) / 2);
    }

    WorkAreaMaximized.Remove(window);
  }

  private static void OnBorderlessWindowStateChanged(object? sender, EventArgs e)
  {
    if (sender is not Window window || window.WindowStyle != WindowStyle.None)
      return;

    if (window.WindowState != WindowState.Maximized)
      return;

    window.StateChanged -= OnBorderlessWindowStateChanged;
    window.WindowState = WindowState.Normal;
    MaximizeToWorkArea(window, saveRestoreBounds: false);
    window.StateChanged += OnBorderlessWindowStateChanged;
  }

  private static Rect GetWorkAreaForWindow(Window window)
  {
    try
    {
      var helper = new WindowInteropHelper(window);
      if (helper.Handle != IntPtr.Zero)
      {
        var monitor = MonitorFromWindow(helper.Handle, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
          var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
          if (GetMonitorInfo(monitor, ref info))
          {
            var source = HwndSource.FromHwnd(helper.Handle);
            if (source?.CompositionTarget is not null)
            {
              var m = source.CompositionTarget.TransformFromDevice;
              var topLeft = m.Transform(new System.Windows.Point(info.rcWork.Left, info.rcWork.Top));
              var bottomRight = m.Transform(new System.Windows.Point(info.rcWork.Right, info.rcWork.Bottom));
              return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            }
          }
        }
      }
    }
    catch
    {
      // Fallback
    }

    return SystemParameters.WorkArea;
  }
}
