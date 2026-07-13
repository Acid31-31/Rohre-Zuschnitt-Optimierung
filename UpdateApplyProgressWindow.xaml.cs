using System.Windows;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class UpdateApplyProgressWindow : Window
{
  private readonly string _stagedRoot;
  private readonly string _targetRoot;
  private readonly int _parentProcessId;

  public UpdateApplyProgressWindow(string stagedRoot, string targetRoot, int parentProcessId = 0)
  {
    InitializeComponent();
    Loaded += UpdateApplyProgressWindow_Loaded;

    _stagedRoot = stagedRoot;
    _targetRoot = targetRoot;
    _parentProcessId = parentProcessId;
  }

  private async void UpdateApplyProgressWindow_Loaded(object sender, RoutedEventArgs e)
  {
    Loaded -= UpdateApplyProgressWindow_Loaded;
    WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    var progress = new Progress<UpdateProgressInfo>(info =>
    {
      InstallProgressBar.Value = info.Percent;
      PercentTextBlock.Text = info.Percent + " %";
      StatusTextBlock.Text = info.Message;
    });

    try
    {
      await Task.Run(
        () => UpdateApplyRunner.ApplyUpdate(_stagedRoot, _targetRoot, progress, CancellationToken.None, _parentProcessId));
      DialogResult = true;
      Close();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Update fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
      DialogResult = false;
      Close();
    }
  }
}
