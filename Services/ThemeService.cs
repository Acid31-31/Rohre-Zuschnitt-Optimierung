using System.IO;
using System.Windows;

namespace RohreZuschnittOptimierung.Services;

public static class ThemeService
{
  private const string SettingsFileName = "theme.txt";
  private static ResourceDictionary? _currentThemeDictionary;

  public static bool IsDarkMode { get; private set; }

  public static void Initialize(Application application)
  {
    var isDark = LoadSavedTheme();
    Apply(application, isDark, persist: false);
  }

  public static void Toggle(Application application)
  {
    Apply(application, !IsDarkMode, persist: true);
  }

  public static void Apply(Application application, bool isDark, bool persist)
  {
    IsDarkMode = isDark;

    var themeUri = new Uri(
      isDark
        ? "pack://application:,,,/Themes/DarkTheme.xaml"
        : "pack://application:,,,/Themes/LightTheme.xaml",
      UriKind.Absolute);

    var themeDictionary = new ResourceDictionary { Source = themeUri };

    if (_currentThemeDictionary is not null)
      application.Resources.MergedDictionaries.Remove(_currentThemeDictionary);

    application.Resources.MergedDictionaries.Add(themeDictionary);
    _currentThemeDictionary = themeDictionary;

    WindowChromeService.ApplyThemeToAllWindows(isDark);

    if (persist)
      SaveTheme(isDark);
  }

  private static bool LoadSavedTheme()
  {
    var portablePath = GetSettingsPath();
    var legacyPath = Path.Combine(AppInfo.LegacyUserDataDirectory, SettingsFileName);

    foreach (var path in new[] { portablePath, legacyPath })
    {
      if (!File.Exists(path))
        continue;

      try
      {
        var value = File.ReadAllText(path).Trim();
        var isDark = string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(path, portablePath, StringComparison.OrdinalIgnoreCase))
          SaveTheme(isDark);

        return isDark;
      }
      catch
      {
      }
    }

    return true;
  }

  private static void SaveTheme(bool isDark)
  {
    try
    {
      var directory = Path.GetDirectoryName(GetSettingsPath())!;
      Directory.CreateDirectory(directory);
      File.WriteAllText(GetSettingsPath(), isDark ? "dark" : "light");
    }
    catch
    {
      // Einstellung ist optional – Fehler ignorieren.
    }
  }

  private static string GetSettingsPath() =>
    Path.Combine(AppInfo.UserDataDirectory, SettingsFileName);
}
