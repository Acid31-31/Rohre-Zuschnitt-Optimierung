using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class LicenseKeyWindow : Window
{
  public LicenseKeyWindow()
  {
    InitializeComponent();

    Title = "KEY_Rohre_Zuschitt " + AppInfo.RevisionLabel;
    TitleTextBlock.Text = "KEY_Rohre_Zuschitt " + AppInfo.RevisionLabel;
    ThisPcCodeTextBox.Text = LicenseActivationService.GetMachineCode();
    ThisPcKeyTextBox.Text = LicenseActivationService.FormatCompatibleKeys();

    Loaded += (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      Dispatcher.BeginInvoke(() =>
      {
        KeyToolChiptunePlayer.Start();
        RefreshMusicButton();
      });
    };
    Closed += (_, _) => KeyToolChiptunePlayer.Stop();
  }

  private void MusicToggle_Click(object sender, RoutedEventArgs e)
  {
    KeyToolChiptunePlayer.Toggle();
    RefreshMusicButton();
  }

  private void RefreshMusicButton()
  {
    MusicToggleButton.Content = KeyToolChiptunePlayer.IsPlaying ? "Musik aus" : "Musik an";
  }

  private void GenerateCustomerKey_Click(object sender, RoutedEventArgs e)
  {
    var codes = LicenseActivationService.SplitMachineCodes(CustomerCodeTextBox.Text);
    if (codes.Count == 0)
    {
      MessageBox.Show(this, "Bitte den PC-Code vom Kunden einfügen (eine Zeile je PC).", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (codes.Count == 1)
      CustomerKeyTextBox.Text = LicenseActivationService.FormatCompatibleKeys(codes[0]);
    else
    {
      var lines = new List<string>();
      foreach (var code in codes)
        lines.Add(code + "  ->  " + LicenseActivationService.FormatCompatibleKeys(code));
      CustomerKeyTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    TryCopy(CustomerKeyTextBox.Text);
  }

  private void CopyThisPcCode_Click(object sender, RoutedEventArgs e) => TryCopy(ThisPcCodeTextBox.Text);

  private void CopyThisPcKey_Click(object sender, RoutedEventArgs e) => TryCopy(ThisPcKeyTextBox.Text);

  private void CopyCustomerKey_Click(object sender, RoutedEventArgs e) => TryCopy(CustomerKeyTextBox.Text);

  private void TryCopy(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return;

    try
    {
      Clipboard.SetText(text);
    }
    catch
    {
      MessageBox.Show(this, "Zwischenablage nicht verfügbar.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
