using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class MainWindow : Window
{
  private readonly ObservableCollection<CutPartEntry> _parts = new();
  private readonly ObservableCollection<StockRemnantEntry> _remnants = new();
  private List<PipeWarehouseStockItem> _warehouseItems = [];
  private PipeProfileDefinition? _cutProfile;
  private string? _cutMaterial;
  private string? _pdfFolderPath;
  private CutPartEntry? _editingPart;
  private CutOptimizationResult? _lastResult;
  private List<CutPartEntry> _lastParts = [];
  private WarehouseReservationResult? _lastReservation;
  private string? _lastOrderReference;

  public MainWindow()
  {
    InitializeComponent();
    PartsGrid.ItemsSource = _parts;
    RemnantsGrid.ItemsSource = _remnants;
    UpdateThemeToggleLabel();

    SourceInitialized += (_, _) =>
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
    Loaded += async (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      InitializeWarehouse();
      await CheckForUpdatesAsync(showIfCurrent: false);
    };
  }

  private void InitializeWarehouse()
  {
    PipeWarehouseStore.EnsureInitialized();
    ReloadWarehouseProfiles();
    UpdateWarehouseStatus();
  }

  private void ReloadWarehouseProfiles()
  {
    _warehouseItems = PipeWarehouseStore.Load();
  }

  private void SelectCutProfile_Click(object sender, RoutedEventArgs e)
  {
    var wizard = new PipeWarehouseAddWizardWindow(WarehouseWizardMode.SelectCutProfile) { Owner = this };
    if (wizard.ShowDialog() != true
        || wizard.Result?.Profile is null
        || string.IsNullOrWhiteSpace(wizard.Result.Material))
      return;

    _cutProfile = wizard.Result.Profile;
    _cutMaterial = wizard.Result.Material;
    CutProfileDisplayTextBlock.Text = $"{_cutProfile.FullLabel} · {_cutMaterial}";
    SelectCutProfileButton.Content = "Profil ändern …";
    SyncRemnantsFromWarehouse();
    UpdateWarehouseStatus();
  }

  private void OpenWarehouse_Click(object sender, RoutedEventArgs e)
  {
    var window = new PipeWarehouseWindow { Owner = this };
    window.ShowDialog();
    ReloadWarehouseProfiles();
    SyncRemnantsFromWarehouse();
    UpdateWarehouseStatus();
  }

  private void OpenOrders_Click(object sender, RoutedEventArgs e)
  {
    var window = new PipeOrdersWindow { Owner = this };
    window.ShowDialog();
    ReloadWarehouseProfiles();
    SyncRemnantsFromWarehouse();
    UpdateWarehouseStatus();
  }

  private void OpenPdfSettings_Click(object sender, RoutedEventArgs e)
  {
    var window = new PdfSettingsWindow { Owner = this };
    window.ShowDialog();
  }

  private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
  {
    ThemeService.Toggle(Application.Current);
    WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
    UpdateThemeToggleLabel();
  }

  private PipeProfileDefinition? GetSelectedCutProfile() => _cutProfile;

  private void SyncRemnantsFromWarehouse()
  {
    if (_cutProfile is null || string.IsNullOrWhiteSpace(_cutMaterial))
      return;

    var stockLengthMm = ParsePositiveDoubleSafe(StockLengthTextBox.Text, CutOptimizationDefaults.StockLengthMm);
    _remnants.Clear();

    foreach (var entry in PipeWarehouseService.BuildStockForOptimization(
               _cutProfile.Id, _cutMaterial, stockLengthMm, _warehouseItems))
    {
      _remnants.Add(new StockRemnantEntry
      {
        LengthMm = entry.LengthMm,
        Quantity = entry.Quantity,
        IsFullBar = entry.IsFullBar
      });
    }
  }

  private void UpdateWarehouseStatus()
  {
    if (_cutProfile is null || string.IsNullOrWhiteSpace(_cutMaterial))
    {
      WarehouseStatusTextBlock.Text = $"{_warehouseItems.Count} Lagerzeilen · Profil und Materialart wählen.";
      return;
    }

    var stock = _warehouseItems
      .Where(item => string.Equals(item.ProfileId, _cutProfile.Id, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(item.Material, _cutMaterial, StringComparison.OrdinalIgnoreCase))
      .ToList();
    var available = stock.Where(item => item.Quantity > 0).Sum(item => item.Quantity);
    var reserved = stock.Where(item => item.ReservedQuantity > 0).Sum(item => item.ReservedQuantity);
    var original = stock
      .Where(item => item.Quantity > 0 && Math.Abs(item.LengthMm - CutOptimizationDefaults.StockLengthMm) < 0.5)
      .Sum(item => item.Quantity);

    WarehouseStatusTextBlock.Text =
      $"{_cutProfile.FullLabel} · {_cutMaterial}: {available} frei, {reserved} reserviert ({original}× 6 m frei) — bei Optimieren wird freies Material reserviert.";
  }

  private static double ParsePositiveDoubleSafe(string text, double fallback)
  {
    if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value > 0)
      return value;

    return fallback;
  }

  private void UpdateThemeToggleLabel()
  {
    ThemeToggleMenuItem.Header = ThemeService.IsDarkMode ? "Hellmodus" : "Dunkelmodus";
  }

  private void ExitApplication_Click(object sender, RoutedEventArgs e) =>
    Close();

  private void ShowAbout_Click(object sender, RoutedEventArgs e)
  {
    MessageBox.Show(
      this,
      AppInfo.ProductName + Environment.NewLine
      + $"Version {AppInfo.DisplayVersion}" + Environment.NewLine + Environment.NewLine
      + "Rohrzuschnitt optimieren, Lagerverwaltung, Auftragsführung, Zuschnittplan-PDF." + Environment.NewLine
      + $"Updates: github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}",
      "Über die Anwendung",
      MessageBoxButton.OK,
      MessageBoxImage.Information);
  }

  private void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
    _ = CheckForUpdatesAsync(showIfCurrent: true);

  private async Task CheckForUpdatesAsync(bool showIfCurrent)
  {
    try
    {
      if (showIfCurrent)
        Mouse.OverrideCursor = Cursors.Wait;

      var update = await GitHubUpdateService.CheckForUpdateAsync();

      if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
      {
        if (showIfCurrent)
        {
          MessageBox.Show(this, update.ErrorMessage, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return;
      }

      if (update.UpdateAvailable)
      {
        var dialog = new UpdateAvailableWindow(update)
        {
          Owner = this,
          WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
        return;
      }

      if (showIfCurrent)
      {
        MessageBox.Show(
          this,
          $"Kein neueres Update gefunden.\n\nInstalliert: {AppInfo.DisplayVersion}\nGitHub: {update.ReleaseTag}",
          "Update-Prüfung",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      }
    }
    catch (Exception ex)
    {
      if (showIfCurrent)
        MessageBox.Show(this, ex.Message, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    finally
    {
      if (showIfCurrent)
        Mouse.OverrideCursor = null;
    }
  }

  private void SetExportPdfEnabled(bool enabled)
  {
    ExportPdfButton.IsEnabled = enabled;
    ExportPdfMenuItem.IsEnabled = enabled;
  }

  private void ChoosePdfFolder_Click(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFolderDialog
    {
      Title = "Ordner mit PDF-Zeichnungen wählen"
    };

    if (dialog.ShowDialog() != true)
      return;

    _pdfFolderPath = dialog.FolderName;
    LoadPdfFiles();
  }

  private void ReloadPdfFolder_Click(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(_pdfFolderPath))
    {
      MessageBox.Show(this, "Bitte zuerst einen PDF-Ordner wählen.", "PDF-Ordner", MessageBoxButton.OK,
        MessageBoxImage.Information);
      return;
    }

    LoadPdfFiles();
  }

  private void LoadPdfFiles()
  {
    if (string.IsNullOrWhiteSpace(_pdfFolderPath) || !Directory.Exists(_pdfFolderPath))
      return;

    var files = Directory.EnumerateFiles(_pdfFolderPath, "*.pdf", SearchOption.TopDirectoryOnly)
      .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
      .Select(path => new PdfFileOption
      {
        FileName = Path.GetFileName(path),
        FullPath = path
      })
      .ToList();

    PdfComboBox.ItemsSource = files;
    PdfFolderTextBlock.Text = $"{files.Count} PDF(s) in: {_pdfFolderPath}";

    if (files.Count > 0)
      PdfComboBox.SelectedIndex = 0;
    else
      AnalysisTextBlock.Text = "Keine PDF-Dateien im gewählten Ordner gefunden.";
  }

  private void PdfComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (PdfComboBox.SelectedItem is not PdfFileOption pdf)
      return;

    try
    {
      var result = PdfDrawingAnalysisService.Analyze(pdf.FullPath);
      AnalysisTextBlock.Text = result.Summary;

      if (result.LengthMm is > 0)
        PartLengthTextBox.Text = FormatInputNumber(result.LengthMm.Value);

      MiterEnd1TextBox.Text = FormatInputNumber(result.MiterEnd1Deg ?? 0);
      MiterEnd2TextBox.Text = FormatInputNumber(result.MiterEnd2Deg ?? 0);
    }
    catch (Exception ex)
    {
      AnalysisTextBlock.Text = $"PDF-Analyse fehlgeschlagen: {ex.Message}";
    }
  }

  private void AddPartFromPdf_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      var part = BuildPartFromInputs(PdfComboBox.SelectedItem as PdfFileOption);

      if (_editingPart is not null)
      {
        _editingPart.DrawingName = part.DrawingName;
        _editingPart.PdfPath = part.PdfPath;
        _editingPart.LengthMm = part.LengthMm;
        _editingPart.MiterEnd1Deg = part.MiterEnd1Deg;
        _editingPart.MiterEnd2Deg = part.MiterEnd2Deg;
        _editingPart.Quantity = part.Quantity;

        var index = _parts.IndexOf(_editingPart);
        if (index >= 0)
        {
          _parts.RemoveAt(index);
          _parts.Insert(index, _editingPart);
        }

        ClearEditingMode();
        return;
      }

      _parts.Add(part);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Eingabe unvollständig", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void EditSelectedPart_Click(object sender, RoutedEventArgs e)
  {
    if (PartsGrid.SelectedItem is not CutPartEntry selected)
    {
      MessageBox.Show(
        this,
        "Bitte zuerst eine Zeile in der Teilliste auswählen.",
        "Bearbeiten",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      return;
    }

    _editingPart = selected;
    PartLengthTextBox.Text = FormatInputNumber(selected.LengthMm);
    PartQuantityTextBox.Text = selected.Quantity.ToString(CultureInfo.InvariantCulture);
    MiterEnd1TextBox.Text = FormatInputNumber(selected.MiterEnd1Deg);
    MiterEnd2TextBox.Text = FormatInputNumber(selected.MiterEnd2Deg);

    if (!string.IsNullOrWhiteSpace(selected.PdfPath))
    {
      var match = (PdfComboBox.ItemsSource as IEnumerable<PdfFileOption>)?
        .FirstOrDefault(item => string.Equals(item.FullPath, selected.PdfPath, StringComparison.OrdinalIgnoreCase));
      if (match is not null)
        PdfComboBox.SelectedItem = match;
    }

    UpdateAddButtonLabel();
    AnalysisTextBlock.Text = string.IsNullOrWhiteSpace(selected.DrawingName)
      ? "Bearbeiten: Werte unten ändern und „Änderung übernehmen“ klicken."
      : $"Bearbeiten: {selected.DrawingName} – Werte ändern und „Änderung übernehmen“ klicken.";
  }

  private void ClearAll_Click(object sender, RoutedEventArgs e)
  {
    if (_parts.Count == 0
        && PlanItemsControl.ItemsSource is null
        && string.IsNullOrWhiteSpace(PartLengthTextBox.Text))
    {
      ResetManualInput();
      return;
    }

    var answer = MessageBox.Show(
      this,
      "Teilliste, Rohreste, manuelle Eingabe und Schnittplan wirklich löschen?",
      "Alles löschen",
      MessageBoxButton.YesNo,
      MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
      return;

    _parts.Clear();
    _remnants.Clear();
    ClearEditingMode();
    ResetManualInput();
    ClearResult();
  }

  private void ClearEditingMode()
  {
    _editingPart = null;
    UpdateAddButtonLabel();
  }

  private void UpdateAddButtonLabel()
  {
    AddPartButton.Content = _editingPart is null
      ? "Zur Teilliste hinzufügen"
      : "Änderung übernehmen";
  }

  private void ResetManualInput()
  {
    PartLengthTextBox.Clear();
    PartQuantityTextBox.Text = "1";
    MiterEnd1TextBox.Text = "0";
    MiterEnd2TextBox.Text = "0";
    AnalysisTextBlock.Text = "PDF auswählen – Länge und Gehrung werden automatisch vorgeschlagen.";
  }

  private void ClearResult()
  {
    _lastResult = null;
    _lastParts = [];
    SummaryTextBlock.Text = "Noch keine Berechnung.";
    PlanItemsControl.ItemsSource = null;
    SetExportPdfEnabled(false);
  }

  private void ExportPdf_Click(object sender, RoutedEventArgs e)
  {
    if (_lastResult is null)
    {
      MessageBox.Show(
        this,
        "Bitte zuerst optimieren – es liegt noch kein Schnittplan vor.",
        "PDF exportieren",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      return;
    }

    var dialog = new SaveFileDialog
    {
      Title = "Zuschnittplan als PDF speichern",
      Filter = "PDF-Datei (*.pdf)|*.pdf",
      FileName = string.IsNullOrWhiteSpace(_lastOrderReference)
        ? $"Zuschnittplan_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        : $"Zuschnittplan_{SanitizeFileNamePart(_lastOrderReference)}.pdf",
      DefaultExt = ".pdf"
    };

    if (dialog.ShowDialog() != true)
      return;

    try
    {
      ExportPdfToFile(dialog.FileName);
      MessageBox.Show(
        this,
        $"PDF gespeichert:\n{dialog.FileName}",
        "PDF exportieren",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "PDF exportieren fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OpenPrintPdf()
  {
    if (_lastResult is null)
      return;

    try
    {
      var path = CreateAutoPdfPath();
      ExportPdfToFile(path);
      Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
      MessageBox.Show(
        this,
        $"PDF konnte nicht geöffnet werden:\n{ex.Message}",
        "PDF öffnen fehlgeschlagen",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }
  }

  private void ExportPdfToFile(string filePath)
  {
    if (_lastResult is null)
      throw new InvalidOperationException("Kein Schnittplan vorhanden.");

    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    CutPlanPdfExportService.Export(filePath, _lastResult, _lastParts, orderReference: _lastOrderReference);
  }

  private string GetOrderReferenceOrWarn()
  {
    var orderReference = OrderReferenceTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(orderReference))
    {
      MessageBox.Show(
        this,
        "Bitte oben eine Auftragsnummer eingeben.",
        "Auftragsnummer fehlt",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      OrderReferenceTextBox.Focus();
      return string.Empty;
    }

    return orderReference;
  }

  private static string SanitizeFileNamePart(string value)
  {
    var trimmed = value.Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
      return "Auftrag";

    var invalid = Path.GetInvalidFileNameChars();
    var chars = trimmed.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
    return new string(chars);
  }

  private string CreateAutoPdfPath()
  {
    var folder = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Rohre-Zuschnitt-Optimierung");
    var suffix = string.IsNullOrWhiteSpace(_lastOrderReference)
      ? DateTime.Now.ToString("yyyyMMdd_HHmmss")
      : SanitizeFileNamePart(_lastOrderReference);
    return Path.Combine(folder, $"Zuschnittplan_{suffix}.pdf");
  }

  private static string CreateAutoOrderPdfPath(string orderReference)
  {
    var folder = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Rohre-Zuschnitt-Optimierung");
    return Path.Combine(folder, $"Bestellliste_{SanitizeFileNamePart(orderReference)}.pdf");
  }

  private void AddPartRow_Click(object sender, RoutedEventArgs e)
  {
    _parts.Add(new CutPartEntry());
  }

  private CutPartEntry BuildPartFromInputs(PdfFileOption? pdf)
  {
    var lengthMm = ParsePositiveDouble(PartLengthTextBox.Text, "Rohrlänge");
    var quantity = ParsePositiveInt(PartQuantityTextBox.Text, "Stückzahl");
    var miterEnd1 = MiterNotation.NormalizeInputAngle(
      ParseNonNegativeDouble(MiterEnd1TextBox.Text, "Gehrung Ende A"));
    var miterEnd2 = MiterNotation.NormalizeInputAngle(
      ParseNonNegativeDouble(MiterEnd2TextBox.Text, "Gehrung Ende B"));

    return new CutPartEntry
    {
      DrawingName = pdf?.FileName,
      PdfPath = pdf?.FullPath,
      LengthMm = lengthMm,
      MiterEnd1Deg = miterEnd1,
      MiterEnd2Deg = miterEnd2,
      Quantity = quantity
    };
  }

  private void Optimize_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      var orderReference = GetOrderReferenceOrWarn();
      if (string.IsNullOrWhiteSpace(orderReference))
        return;

      var stockLengthMm = ParsePositiveDouble(StockLengthTextBox.Text, "Originalstange");
      var kerfMm = ParseNonNegativeDouble(KerfTextBox.Text, "Schnittbreite");
      var profile = GetSelectedCutProfile();

      if (profile is null || string.IsNullOrWhiteSpace(_cutMaterial))
      {
        MessageBox.Show(
          this,
          "Bitte zuerst Rohrprofil und Materialart wählen (Button „Profil wählen …“).",
          "Profil fehlt",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      _warehouseItems = PipeWarehouseStore.Load();
      var remnants = PipeWarehouseService.BuildStockForOptimization(
        profile.Id, _cutMaterial, stockLengthMm, _warehouseItems);
      SyncRemnantsFromWarehouse();

      var parts = CollectPartsForOptimization();

      if (parts.Count == 0)
      {
        MessageBox.Show(
          this,
          "Bitte Rohrlänge und Stückzahl eingeben – entweder in der manuellen Eingabe oder in der Teilliste.",
          "Eingabe fehlt",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      var result = CutOptimizationService.Optimize(stockLengthMm, kerfMm, parts, remnants);
      _lastResult = result;
      _lastParts = parts;
      _lastReservation = null;
      _lastOrderReference = orderReference;

      if (profile is not null && !string.IsNullOrWhiteSpace(_cutMaterial))
      {
        var deferWarehouseBooking = result.OrderedNewBarsCount > 0;
        var orderLines = PipeWarehouseService.BuildOrderList(profile.Id, _cutMaterial, result, profile);

        if (!deferWarehouseBooking)
        {
          _lastReservation = PipeWarehouseService.ReserveOptimizationResult(
            profile.Id, _cutMaterial, result, _warehouseItems, orderReference);
          ReloadWarehouseProfiles();
          SyncRemnantsFromWarehouse();
          UpdateWarehouseStatus();
        }

        PipeOrderService.SaveFromOptimization(
          orderReference,
          profile,
          _cutMaterial,
          stockLengthMm,
          kerfMm,
          parts,
          result,
          _lastReservation,
          warehouseBooked: !deferWarehouseBooking);

        if (orderLines.Count > 0)
        {
          var emptyReservation = _lastReservation ?? new WarehouseReservationResult { OrderReference = orderReference };
          var orderPdfPath = CreateAutoOrderPdfPath(orderReference);
          OrderListPdfExportService.Export(
            orderPdfPath,
            orderReference,
            profile,
            _cutMaterial,
            emptyReservation,
            orderLines,
            result);

          try
          {
            Process.Start(new ProcessStartInfo(orderPdfPath) { UseShellExecute = true });
          }
          catch (Exception ex)
          {
            MessageBox.Show(
              this,
              $"Bestellliste konnte nicht geöffnet werden:\n{ex.Message}",
              "Bestellliste",
              MessageBoxButton.OK,
              MessageBoxImage.Warning);
          }

          MessageBox.Show(
            this,
            PipeWarehouseService.FormatOrderList(orderLines) + Environment.NewLine + Environment.NewLine
            + $"Auftrag: {orderReference}" + Environment.NewLine
            + $"Bestellliste-PDF: {orderPdfPath}" + Environment.NewLine + Environment.NewLine
            + "Nach Lieferung: Menü „Lager → Aufträge“ → Material eingetroffen → nach dem Schneiden „Schnitt verbuchen“.",
            "Material fehlt — Bestellliste erstellt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        }
        else if (_lastReservation?.ReservedBarsCount > 0 || _lastReservation?.ReturnedRemnantCount > 0)
        {
          MessageBox.Show(
            this,
            PipeWarehouseService.FormatReservationSummary(_lastReservation!) + Environment.NewLine + Environment.NewLine
            + $"Auftrag: {orderReference}",
            "Lager reserviert",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        }
      }

      ShowResult(result);
      OpenPrintPdf();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Berechnung fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private List<StockRemnantEntry> CollectRemnantsForOptimization() =>
    _remnants
      .Where(r => r.LengthMm > 0 && r.Quantity > 0)
      .ToList();

  private List<CutPartEntry> CollectPartsForOptimization()
  {
    var parts = _parts
      .Where(part => part.LengthMm > 0 && part.Quantity > 0)
      .ToList();

    if (!TryBuildPendingPartFromInputs(PdfComboBox.SelectedItem as PdfFileOption, out var pending))
      return parts;

    var alreadyListed = parts.Any(part =>
      Math.Abs(part.LengthMm - pending.LengthMm) < 0.01
      && part.Quantity == pending.Quantity
      && Math.Abs(part.MiterEnd1Deg - pending.MiterEnd1Deg) < 0.01
      && Math.Abs(part.MiterEnd2Deg - pending.MiterEnd2Deg) < 0.01
      && string.Equals(part.DrawingName ?? string.Empty, pending.DrawingName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    if (alreadyListed)
      return parts;

    parts.Add(pending);
    _parts.Add(pending);
    return parts;
  }

  private bool TryBuildPendingPartFromInputs(PdfFileOption? pdf, out CutPartEntry part)
  {
    part = null!;

    if (string.IsNullOrWhiteSpace(PartLengthTextBox.Text))
      return false;

    if (!double.TryParse(PartLengthTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lengthMm)
        || lengthMm <= 0)
      return false;

    if (!int.TryParse(PartQuantityTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity)
        || quantity <= 0)
      return false;

    try
    {
      part = BuildPartFromInputs(pdf);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private void ShowResult(CutOptimizationResult result)
  {
    var totalPieces = result.Bars.Sum(bar => bar.Pieces.Count);
    var summary =
      $"{result.TotalBars} Stange(n) · {totalPieces} Teil(e) · Verschnitt gesamt: {FormatMm(result.TotalWasteMm)}";

    if (result.OrderedNewBarsCount > 0)
      summary += Environment.NewLine
                 + $"⚠ {result.OrderedNewBarsCount} Stange(n) fehlen — Bestellliste-PDF wird erstellt";

    if (_lastReservation?.ReservedBarsCount > 0)
      summary += Environment.NewLine + $"✓ {_lastReservation.ReservedBarsCount} Stange(n) aus Lager reserviert";

    if (_lastReservation?.ReturnedRemnantCount > 0)
      summary += Environment.NewLine + $"↩ {_lastReservation.ReturnedRemnantCount} Rohrest(e) ins Lager gebucht";

    SummaryTextBlock.Text = summary + Environment.NewLine + result.SawPlanSummary;

    PlanItemsControl.ItemsSource = result.Bars
      .Select(bar => new CutBarPlanViewModel
      {
        Bar = bar,
        StockLengthMm = bar.StockLengthMm,
        KerfMm = result.KerfMm
      })
      .ToList();
    SetExportPdfEnabled(true);
  }

  private static int ParsePositiveInt(string text, string fieldName)
  {
    if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
      throw new InvalidOperationException($"{fieldName} muss eine ganze Zahl größer als 0 sein.");

    return value;
  }

  private static double ParsePositiveDouble(string text, string fieldName)
  {
    if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        || value <= 0)
      throw new InvalidOperationException($"{fieldName} muss eine Zahl größer als 0 sein.");

    return value;
  }

  private static double ParseNonNegativeDouble(string text, string fieldName)
  {
    if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        || value < 0)
      throw new InvalidOperationException($"{fieldName} muss 0 oder größer sein.");

    return value;
  }

  private static string FormatInputNumber(double value) =>
    Math.Abs(value - Math.Round(value)) < 0.01 ? $"{value:0}" : $"{value:0.##}";

  private static string FormatMm(double valueMm)
  {
    if (Math.Abs(valueMm - Math.Round(valueMm)) < 0.01)
      return $"{valueMm:0} mm";

    return $"{valueMm:0.##} mm";
  }
}
