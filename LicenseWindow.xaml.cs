using System.Windows;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class LicenseWindow : Window
{
  public bool ActivatedFullVersion { get; private set; }

  public LicenseWindow()
  {
    InitializeComponent();
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

    PcCodeTextBox.Text = LicenseActivationService.GetMachineCode();
    RefreshStatus();
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

  private void RefreshStatus()
  {
    var status = TrialLicenseService.Evaluate();
    var activated = LicenseActivationService.IsActivated() || !status.IsTrialEdition;

    if (activated)
    {
      StatusTextBlock.Text = "Diese Installation ist als Vollversion freigeschaltet.";
      UnlockHeadingTextBlock.Visibility = Visibility.Collapsed;
      LicenseKeyTextBox.Visibility = Visibility.Collapsed;
      ActivateButton.Visibility = Visibility.Collapsed;
      return;
    }

      StatusTextBlock.Text = status.SummaryText
      + "\nEinzel-Lizenz: den PC-Code kopieren und an den Anbieter senden."
      + " Der Schlüssel gilt nur für diesen Rechner. Weitere PCs brauchen jeweils einen eigenen Code.";
  }

  private void CopyPcCode_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      Clipboard.SetText(PcCodeTextBox.Text);
    }
    catch
    {
      MessageBox.Show(this, "Zwischenablage nicht verfügbar.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void Activate_Click(object sender, RoutedEventArgs e)
  {
    if (!LicenseActivationService.TryActivate(LicenseKeyTextBox.Text, out var message))
    {
      MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    ActivatedFullVersion = true;
    MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Information);
    RefreshStatus();
    DialogResult = true;
    Close();
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
