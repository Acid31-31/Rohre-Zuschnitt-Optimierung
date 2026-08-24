using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class OptimizePreviewWindow : Window
{
  public bool Confirmed { get; private set; }

  private readonly IReadOnlyList<OptimizePreviewItem> _items;

  public OptimizePreviewWindow(
    string orderReference,
    string profileLabel,
    string material,
    double stockLengthMm,
    IReadOnlyList<CutPartEntry> parts)
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    _items = parts
      .OrderByDescending(part => part.LengthMm)
      .ThenBy(part => part.DrawingName, StringComparer.OrdinalIgnoreCase)
      .Select(part => new OptimizePreviewItem
      {
        DrawingName = string.IsNullOrWhiteSpace(part.DrawingName) ? "Manuelle Eingabe" : part.DrawingName!,
        PdfPath = part.PdfPath,
        LengthMm = part.LengthMm,
        Quantity = part.Quantity,
        MiterText = MiterNotation.Format(part.MiterEnd1Deg, part.MiterEnd2Deg),
        IsTooLong = part.LengthMm > stockLengthMm,
        LengthMissing = part.LengthMm <= 0
      })
      .ToList();

    PartsGrid.ItemsSource = _items;

    var totalPieces = parts.Sum(part => part.Quantity);
    var tooLong = _items.Where(item => item.IsTooLong).ToList();
    var missing = _items.Where(item => item.LengthMissing).ToList();
    SummaryTextBlock.Text =
      $"Auftrag: {orderReference}" + Environment.NewLine
      + $"Profil: {profileLabel} · {material}" + Environment.NewLine
      + $"Originalstange: {stockLengthMm.ToString("0.###", CultureInfo.InvariantCulture)} mm"
      + $" · {parts.Count} Positionszeile(n) · {totalPieces} Stück gesamt";

    if (tooLong.Count > 0 || missing.Count > 0)
    {
      WarningTextBlock.Visibility = Visibility.Visible;
      var lines = new List<string>();
      if (tooLong.Count > 0)
        lines.Add($"{tooLong.Count} Teil(e) länger als {stockLengthMm:0.###} mm – in der Liste rot markiert. Zeichnung rechts prüfen und Länge korrigieren.");
      if (missing.Count > 0)
        lines.Add($"{missing.Count} Teil(e) ohne erkannte Länge.");
      WarningTextBlock.Text = string.Join(Environment.NewLine, lines);
      ContinueButton.IsEnabled = false;
      ContinueButton.ToolTip = "Erst die markierten Teile korrigieren.";
    }
    else
    {
      WarningTextBlock.Visibility = Visibility.Collapsed;
      ContinueButton.IsEnabled = true;
    }

    if (_items.Count > 0)
      PartsGrid.SelectedIndex = 0;
  }

  private void PartsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (PartsGrid.SelectedItem is not OptimizePreviewItem item)
    {
      PreviewImage.Source = null;
      PreviewTitleTextBlock.Text = "PDF-Vorschau";
      PreviewStatusTextBlock.Text = "Zeichnung in der Liste auswählen.";
      return;
    }

    PreviewTitleTextBlock.Text = item.DrawingName;
    if (string.IsNullOrWhiteSpace(item.PdfPath) || !File.Exists(item.PdfPath))
    {
      PreviewImage.Source = null;
      PreviewStatusTextBlock.Text = "Keine PDF-Datei für dieses Teil hinterlegt.";
      return;
    }

    if (PdfPreviewService.TryRenderFirstPage(item.PdfPath, out var image, out var error))
    {
      PreviewImage.Source = image;
      PreviewStatusTextBlock.Text =
        item.LengthText + " · " + item.MiterText + " · " + item.StatusText;
    }
    else
    {
      PreviewImage.Source = null;
      PreviewStatusTextBlock.Text = error + "  → „PDF öffnen“ nutzen.";
    }
  }

  private void OpenPdf_Click(object sender, RoutedEventArgs e)
  {
    if (PartsGrid.SelectedItem is not OptimizePreviewItem item
        || string.IsNullOrWhiteSpace(item.PdfPath)
        || !File.Exists(item.PdfPath))
    {
      MessageBox.Show(this, "Keine PDF-Datei für die Auswahl gefunden.", "PDF öffnen",
        MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    try
    {
      Process.Start(new ProcessStartInfo(item.PdfPath) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "PDF öffnen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void Continue_Click(object sender, RoutedEventArgs e)
  {
    Confirmed = true;
    DialogResult = true;
    Close();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    Confirmed = false;
    DialogResult = false;
    Close();
  }
}
