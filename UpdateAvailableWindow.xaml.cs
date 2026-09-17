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

    var sizeText = _update.AssetSizeBytes > 0 ? _update.AssetSizeDisplay : string.Empty;
    SizeTextBlock.Text = string.IsNullOrWhiteSpace(sizeText)
      ? "Update-Größe: wird beim Download angezeigt"
      : "Update-Größe: " + sizeText;

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
    RemainingTimeTextBlock.Text = "Restlaufzeit wird berechnet…";
    DownloadSizeTextBlock.Text = string.IsNullOrWhiteSpace(_update.AssetSizeDisplay)
      ? "Update-Größe: wird geladen…"
      : "Update-Größe: " + _update.AssetSizeDisplay;

    try
    {
      var presenter = new UpdateProgressPresenter((percent, message, remaining) =>
      {
        UpdateProgressBar.Value = percent;
        PercentTextBlock.Text = percent + " %";
        StatusTextBlock.Text = message;
        RemainingTimeTextBlock.Text = remaining;
      });
      var progress = new Progress<UpdateProgressInfo>(info =>
      {
        presenter.Report(info);
        if (info.TotalBytes > 0)
        {
          DownloadSizeTextBlock.Text = AppUpdateInfo.FormatMegabytes(info.BytesRead)
                                       + " von " + AppUpdateInfo.FormatMegabytes(info.TotalBytes);
        }
        else if (info.BytesRead > 0)
        {
          DownloadSizeTextBlock.Text = AppUpdateInfo.FormatMegabytes(info.BytesRead) + " geladen";
        }
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
