using System.Windows;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class UpdateAvailableWindow : Window
{
  private readonly AppUpdateInfo _update;
  private bool _isUpdating;

  public UpdateAvailableWindow(AppUpdateInfo update)
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    _update = update;
    VersionTextBlock.Text =
      $"Installiert: {AppInfo.DisplayVersion}   →   Neu: {(_update.ReleaseTag.Length > 0 ? _update.ReleaseTag : "unbekannt")}";

    var changeItems = ReleaseNotesFormatter.ExtractChangeItems(_update.ReleaseNotes);
    if (changeItems.Count > 0)
    {
      ChangesItemsControl.ItemsSource = changeItems
        .Select(item => "• " + item)
        .ToList();
      NotesTextBlock.Visibility = Visibility.Collapsed;
    }
    else
    {
      ChangesItemsControl.Visibility = Visibility.Collapsed;
      NotesTextBlock.Visibility = Visibility.Visible;
      NotesTextBlock.Text = ReleaseNotesFormatter.FormatForDisplay(_update.ReleaseNotes);
    }
  }

  private async void UpdateButton_Click(object sender, RoutedEventArgs e)
  {
    if (_isUpdating)
      return;

    _isUpdating = true;
    UpdateButton.IsEnabled = false;
    LaterButton.IsEnabled = false;
    NotesBorder.Visibility = Visibility.Collapsed;
    ProgressPanel.Visibility = Visibility.Visible;
    UpdateProgressBar.Value = 0;
    PercentTextBlock.Text = "0 %";
    StatusTextBlock.Text = "Update wird vorbereitet…";

    try
    {
      var progress = new Progress<UpdateProgressInfo>(info =>
      {
        UpdateProgressBar.Value = info.Percent;
        PercentTextBlock.Text = info.Percent + " %";
        StatusTextBlock.Text = info.Message;
      });

      var stagedRoot = await GitHubUpdateService.DownloadAndStageUpdateAsync(_update, progress);
      StatusTextBlock.Text = "Installation wird gestartet…";
      PercentTextBlock.Text = "100 %";
      UpdateProgressBar.Value = 100;
      GitHubUpdateService.LaunchUpdaterAndShutdown(stagedRoot);
    }
    catch (Exception ex)
    {
      _isUpdating = false;
      UpdateButton.IsEnabled = true;
      LaterButton.IsEnabled = true;
      NotesBorder.Visibility = Visibility.Visible;
      ProgressPanel.Visibility = Visibility.Collapsed;
      MessageBox.Show(this, ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();
}
