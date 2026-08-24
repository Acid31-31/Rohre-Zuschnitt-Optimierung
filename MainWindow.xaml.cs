using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
  private string? _pdfImportRoot;
  private Brush? _pdfDropBorderDefaultBrush;
  private CutPartEntry? _editingPart;
  private CutOptimizationResult? _lastResult;
  private List<CutPartEntry> _lastParts = [];
  private WarehouseReservationResult? _lastReservation;
  private string? _lastOrderReference;
  private readonly TrialLicenseStatus _trialStatus;

  public MainWindow() : this(TrialLicenseService.Evaluate())
  {
  }

  public MainWindow(TrialLicenseStatus trialStatus)
  {
    _trialStatus = trialStatus;
    InitializeComponent();
    Title = AppInfo.ProductName + trialStatus.TitleSuffix;
    PartsGrid.ItemsSource = _parts;
    RemnantsGrid.ItemsSource = _remnants;
    UpdateThemeToggleLabel();

    SourceInitialized += (_, _) =>
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
    Loaded += async (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      DesktopShortcutService.TryRepairToCurrentExe(out _);
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

    ApplyCutProfile(wizard.Result.Profile, wizard.Result.Material);
  }

  private void ApplyCutProfile(PipeProfileDefinition profile, string material)
  {
    _cutProfile = profile;
    _cutMaterial = material;
    CutProfileDisplayTextBlock.Text = $"{_cutProfile.FullLabel} · {_cutMaterial}";
    SelectCutProfileButton.Content = "Profil ändern …";
    SyncRemnantsFromWarehouse();
    UpdateWarehouseStatus();
  }

  private bool TryApplyDetectedProfile(PdfDrawingAnalysisResult analysis, bool overwriteExisting, List<string>? notes = null)
  {
    if (analysis.Profile is null)
      return false;

    var material = !string.IsNullOrWhiteSpace(analysis.Material)
      ? analysis.Material!
      : (_cutMaterial ?? PipeMaterialTypes.Steel);

    if (!overwriteExisting
        && _cutProfile is not null
        && !string.IsNullOrWhiteSpace(_cutMaterial))
    {
      if (!string.Equals(_cutProfile.Id, analysis.Profile.Id, StringComparison.OrdinalIgnoreCase)
          || !string.Equals(_cutMaterial, material, StringComparison.OrdinalIgnoreCase))
      {
        notes?.Add($"{analysis.Profile.FullLabel} · {material}: anderes Profil als aktuell gewählt");
      }

      return false;
    }

    ApplyCutProfile(analysis.Profile, material);
    notes?.Add($"Profil gesetzt: {analysis.Profile.FullLabel} · {material}");
    return true;
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
    if (window.ShowDialog() == true)
      SyncRemnantsFromWarehouse();
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

    var stockLengthMm = AppSettingsStore.Load().StockLengthMm;
    if (stockLengthMm <= 0)
      stockLengthMm = CutOptimizationDefaults.StockLengthMm;
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
    var stockLengthMm = AppSettingsStore.Load().StockLengthMm;
    if (stockLengthMm <= 0)
      stockLengthMm = CutOptimizationDefaults.StockLengthMm;
    var original = stock
      .Where(item => item.Quantity > 0 && Math.Abs(item.LengthMm - stockLengthMm) < 0.5)
      .Sum(item => item.Quantity);

    WarehouseStatusTextBlock.Text =
      $"{_cutProfile.FullLabel} · {_cutMaterial}: {available} frei, {reserved} reserviert ({original}× {FormatMm(stockLengthMm)} frei) — bei Optimieren wird freies Material reserviert.";
  }

  private void UpdateThemeToggleLabel()
  {
    var isDark = ThemeService.IsDarkMode;
    var menuLabel = isDark ? "Hellmodus" : "Dunkelmodus";
    var buttonLabel = isDark ? "☀ Hellmodus" : "🌙 Dunkelmodus";

    ThemeToggleMenuItem.Header = menuLabel;
    ThemeToggleHeaderButton.Content = buttonLabel;
  }

  private void ExitApplication_Click(object sender, RoutedEventArgs e) =>
    Close();

  private void ShowAbout_Click(object sender, RoutedEventArgs e)
  {
    var trialLine = _trialStatus.IsTrialEdition
      ? _trialStatus.SummaryText + Environment.NewLine
      : string.Empty;

    MessageBox.Show(
      this,
      AppInfo.ProductName + Environment.NewLine
      + $"Version {AppInfo.DisplayVersion}" + Environment.NewLine
      + _trialStatus.VersionLine + Environment.NewLine + Environment.NewLine
      + trialLine
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

  private void PdfDropBorder_DragEnter(object sender, DragEventArgs e) =>
    HighlightPdfDrop(e, highlight: true);

  private void PdfDropBorder_DragOver(object sender, DragEventArgs e) =>
    HighlightPdfDrop(e, highlight: true);

  private void PdfDropBorder_DragLeave(object sender, DragEventArgs e) =>
    ResetPdfDropHighlight();

  private void PdfDropBorder_Drop(object sender, DragEventArgs e)
  {
    ResetPdfDropHighlight();
    if (!TryGetDroppedPaths(e, out var paths))
      return;

    _ = ImportDrawingFilesAsync(paths);
  }

  private void PdfDropBorder_Click(object sender, MouseButtonEventArgs e)
  {
    var dialog = new OpenFileDialog
    {
      Title = "PDF-/ZIP-Zeichnungen und optional Excel wählen",
      Filter =
        "Zeichnungen + Excel (*.pdf;*.zip;*.xlsx;*.xlsm;*.xls)|*.pdf;*.zip;*.xlsx;*.xlsm;*.xls|PDF/ZIP (*.pdf;*.zip)|*.pdf;*.zip|Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls",
      Multiselect = true
    };

    if (dialog.ShowDialog() != true)
      return;

    _ = ImportDrawingFilesAsync(dialog.FileNames);
  }

  private void HighlightPdfDrop(DragEventArgs e, bool highlight)
  {
    if (!TryGetDroppedPaths(e, out _))
    {
      e.Effects = DragDropEffects.None;
      e.Handled = true;
      return;
    }

    e.Effects = DragDropEffects.Copy;
    e.Handled = true;

    if (!highlight)
      return;

    _pdfDropBorderDefaultBrush ??= PdfDropBorder.BorderBrush;
    PdfDropBorder.BorderBrush = (Brush)FindResource("AccentBrush");
  }

  private void ResetPdfDropHighlight()
  {
    if (_pdfDropBorderDefaultBrush is not null)
      PdfDropBorder.BorderBrush = _pdfDropBorderDefaultBrush;
  }

  private static bool TryGetDroppedPaths(DragEventArgs e, out string[] paths)
  {
    paths = [];
    if (!e.Data.GetDataPresent(DataFormats.FileDrop))
      return false;

    paths = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? [];
    return paths.Any(IsSupportedDrawingImportPath);
  }

  private static bool IsSupportedDrawingImportPath(string path)
  {
    var extension = Path.GetExtension(path);
    return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
           || IsExcelImportExtension(extension);
  }

  private static bool IsExcelImportExtension(string extension) =>
    extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);

  private async Task ImportDrawingFilesAsync(IEnumerable<string> sourcePaths)
  {
    try
    {
      Mouse.OverrideCursor = Cursors.Wait;
      PdfImportExpander.IsExpanded = true;
      var pdfPaths = CollectPdfPathsFromImport(sourcePaths);
      var files = pdfPaths
        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
        .Select(path => new PdfFileOption
        {
          FileName = Path.GetFileName(path),
          FullPath = path
        })
        .ToList();

      var notes = new List<string>();
      IReadOnlyList<ExcelOrderQuantityRow> excelQuantities = [];
      string? excelName = null;
      try
      {
        var excelPath = ExcelOrderQuantityService.FindExcelFile(_pdfImportRoot ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(excelPath))
        {
          excelQuantities = ExcelOrderQuantityService.LoadQuantities(excelPath);
          excelName = Path.GetFileName(excelPath);
        }
      }
      catch (Exception ex)
      {
        notes.Add("Excel: " + ex.Message);
      }

      var excelPipeCount = excelQuantities.Count(row => row.IsPipe);
      if (files.Count == 0 && excelPipeCount == 0)
      {
        MessageBox.Show(
          this,
          "Keine PDF-Zeichnungen und keine Rohre in der Excel gefunden.\n\nBitte PDF/ZIP und ggf. die Bestell-Excel ablegen.",
          "Zeichnungen",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      PdfComboBox.ItemsSource = files;

      PdfFolderTextBlock.Text = excelName is null
        ? $"{files.Count} Zeichnung(en) geladen – Rohre werden erkannt …"
        : $"{files.Count} Zeichnung(en) + Excel „{excelName}“ ({excelQuantities.Count} Positionen, {excelPipeCount} Rohr) – Rohre werden erkannt …";

      var added = 0;
      var skippedNotPipe = 0;
      var skippedWrongProfile = 0;
      var quantityFromExcel = 0;
      var localAiHits = 0;
      var appSettings = AppSettingsStore.Load();
      var analyses = new List<(PdfFileOption File, PdfDrawingAnalysisResult Analysis)>();
      var profileVotes = new Dictionary<string, (PipeProfileDefinition Profile, string Material, int Count)>(StringComparer.OrdinalIgnoreCase);

      foreach (var file in files)
      {
        try
        {
          var analysis = PdfDrawingAnalysisService.Analyze(file.FullPath);
          if (analysis.IsPipe && appSettings.LocalAiEnabled)
          {
            var ai = await LocalVisionCutAnalysisService.TryEnrichAsync(file.FullPath, analysis, appSettings)
              .ConfigureAwait(true);
            if (ai is not null)
            {
              if (!string.IsNullOrWhiteSpace(ai.Note) && !ai.UsedLocalAi)
                notes.Add($"{file.FileName}: {ai.Note}");

              var merged = LocalVisionCutAnalysisService.Merge(analysis, ai);
              if (!ReferenceEquals(merged, analysis)
                  && (merged.LengthSource == AnalysisValueSource.LocalAi
                      || merged.MiterSource == AnalysisValueSource.LocalAi))
              {
                localAiHits++;
                notes.Add($"{file.FileName}: Länge/Gehrung per Vision-KI ergänzt.");
              }

              analysis = merged;
            }
          }

          analyses.Add((file, analysis));
          if (!analysis.IsPipe || analysis.Profile is null)
            continue;

          var material = analysis.Material ?? PipeMaterialTypes.Steel;
          var key = analysis.Profile.Id + "|" + material;
          if (profileVotes.TryGetValue(key, out var vote))
            profileVotes[key] = (vote.Profile, vote.Material, vote.Count + 1);
          else
            profileVotes[key] = (analysis.Profile, material, 1);
        }
        catch (Exception ex)
        {
          notes.Add($"{file.FileName}: {ex.Message}");
        }
      }

      if (profileVotes.Count > 0)
      {
        var winner = profileVotes.Values.OrderByDescending(entry => entry.Count).First();
        TryApplyDetectedProfile(
          new PdfDrawingAnalysisResult
          {
            Profile = winner.Profile,
            Material = winner.Material,
            Kind = DrawingPartKind.Pipe
          },
          overwriteExisting: _cutProfile is null,
          notes);
      }

      foreach (var (file, analysis) in analyses)
      {
        try
        {
          if (!analysis.IsPipe)
          {
            skippedNotPipe++;
            continue;
          }

          if (_cutProfile is not null
              && analysis.Profile is not null
              && !string.Equals(_cutProfile.Id, analysis.Profile.Id, StringComparison.OrdinalIgnoreCase))
          {
            skippedWrongProfile++;
            notes.Add($"{file.FileName}: {analysis.Profile.FullLabel} – anderes Maß als {_cutProfile.FullLabel}");
            continue;
          }

          if (analysis.LengthMm is not > 0)
          {
            notes.Add($"{file.FileName}: Rohr erkannt"
                      + (!string.IsNullOrWhiteSpace(analysis.PartName) ? $" ({analysis.PartName})" : string.Empty)
                      + ", aber keine Rohrlänge in PDF/STEP – bitte manuell eintragen"
                      + (analysis.Profile is not null ? $" (Profil: {analysis.Profile.FullLabel})" : string.Empty));
            continue;
          }

          var quantity = 1;
          var excelQty = ExcelOrderQuantityService.FindQuantityForDrawing(excelQuantities, file.FileName);
          if (excelQty is > 0)
          {
            quantity = excelQty.Value;
            quantityFromExcel++;
          }

          var part = new CutPartEntry
          {
            DrawingName = file.FileName,
            PdfPath = file.FullPath,
            LengthMm = analysis.LengthMm.Value,
            MiterEnd1Deg = MiterNotation.NormalizeInputAngle(analysis.MiterEnd1Deg ?? 0),
            MiterEnd2Deg = MiterNotation.NormalizeInputAngle(analysis.MiterEnd2Deg ?? 0),
            Quantity = quantity
          };

          var existingSamePdf = _parts.FirstOrDefault(existing =>
            string.Equals(existing.PdfPath ?? string.Empty, part.PdfPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing.DrawingName ?? string.Empty, part.DrawingName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
          if (existingSamePdf is not null)
          {
            var index = _parts.IndexOf(existingSamePdf);
            if (index >= 0)
              _parts[index] = part;
            added++;
            notes.Add($"{file.FileName}: Teilliste aktualisiert (Länge {part.LengthMm:0.##} mm, {part.Quantity} Stück)");
            continue;
          }

          _parts.Add(part);
          added++;
        }
        catch (Exception ex)
        {
          notes.Add($"{file.FileName}: {ex.Message}");
        }
      }

      if (_cutProfile is null)
      {
        var excelVotes = new Dictionary<string, (PipeProfileDefinition Profile, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in excelQuantities.Where(entry => entry.IsPipe && entry.Profile is not null))
        {
          var key = row.Profile!.Id;
          if (excelVotes.TryGetValue(key, out var vote))
            excelVotes[key] = (vote.Profile, vote.Count + 1);
          else
            excelVotes[key] = (row.Profile, 1);
        }

        if (excelVotes.Count > 0)
        {
          var winner = excelVotes.Values.OrderByDescending(entry => entry.Count).First();
          TryApplyDetectedProfile(
            new PdfDrawingAnalysisResult
            {
              Profile = winner.Profile,
              Material = PipeMaterialTypes.Steel,
              Kind = DrawingPartKind.Pipe
            },
            overwriteExisting: true,
            notes);
        }
      }

      var generated = AddWorkshopTubesFromExcel(excelQuantities, files, notes, ref quantityFromExcel);
      added += generated;

      PdfComboBox.ItemsSource = files;
      if (files.Count > 0)
        PdfComboBox.SelectedIndex = 0;

      PdfFolderTextBlock.Text = $"{files.Count} Zeichnung(en) · {added} Rohr(e) in Teilliste"
                                + (skippedNotPipe > 0 ? $" · {skippedNotPipe} Blech/kein Rohr" : string.Empty)
                                + (skippedWrongProfile > 0 ? $" · {skippedWrongProfile} anderes Profilmaß" : string.Empty)
                                + (generated > 0 ? $" · {generated} Werkstattzeichnung(en)" : string.Empty)
                                + (quantityFromExcel > 0 ? $" · {quantityFromExcel}× Menge aus Excel" : string.Empty)
                                + (localAiHits > 0 ? $" · {localAiHits}× Vision-KI" : string.Empty);

      var profileLine = _cutProfile is not null
        ? $"Aktives Profil: {_cutProfile.FullLabel} · {_cutMaterial}"
        : "Kein Profil erkannt – bitte manuell wählen.";

      var excelLine = excelName is null
        ? "Keine Excel-Datei gefunden (ZIP/Dateien enthielten keine .xlsx) – Stückzahl = 1. Excel ggf. zusätzlich mit ablegen."
        : quantityFromExcel > 0
          ? $"Bestellmenge aus „{excelName}“ für {quantityFromExcel} Position(en) übernommen."
            + (generated > 0 ? $" {generated} Rohr(e) ohne Original-PDF als Werkstattzeichnung erzeugt." : string.Empty)
          : $"Excel „{excelName}“ geladen, aber keine Zeichnung konnte der Bestellmenge zugeordnet werden.";

      var skipLine = skippedNotPipe > 0
        ? $"{skippedNotPipe} Zeichnung(en) sind kein Rohr (Blech, Abdeckung, Halter o. Ä.) und wurden nicht übernommen."
        : string.Empty;

      AnalysisTextBlock.Text = (added > 0
        ? $"{added} Rohr(e) in die Teilliste übernommen."
        : "Keine Rohrzeichnungen erkannt – Bleche werden nicht als Rohre übernommen.")
        + Environment.NewLine + profileLine
        + Environment.NewLine + excelLine
        + (skipLine.Length > 0 ? Environment.NewLine + skipLine : string.Empty)
        + (notes.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, notes.Take(10)) : string.Empty);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Zeichnungen importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    finally
    {
      Mouse.OverrideCursor = null;
    }
  }

  private int AddWorkshopTubesFromExcel(
    IReadOnlyList<ExcelOrderQuantityRow> excelRows,
    List<PdfFileOption> files,
    List<string> notes,
    ref int quantityFromExcel)
  {
    var pipeRows = excelRows.Where(row => row.IsPipe && row.Quantity > 0).ToList();
    if (pipeRows.Count == 0 || string.IsNullOrWhiteSpace(_pdfImportRoot))
      return 0;

    var existingPdfs = files.ToList();
    var bom = AssemblyTubeBomService.Extract(existingPdfs.Select(file => file.FullPath)).ToList();
    var bomUsed = new bool[bom.Count];
    for (var i = 0; i < bom.Count; i++)
    {
      if (!string.IsNullOrWhiteSpace(bom[i].DrawingNumber)
          && existingPdfs.Any(pdf => ExcelOrderQuantityService.MatchesDrawing(bom[i].DrawingNumber!, pdf.FileName)))
        bomUsed[i] = true;
    }

    var generated = 0;
    var sequence = 1;
    var orderReference = OrderReferenceTextBox.Text.Trim();

    foreach (var row in pipeRows)
    {
      if (!string.IsNullOrWhiteSpace(row.DrawingNumber)
          && (existingPdfs.Any(pdf => ExcelOrderQuantityService.MatchesDrawing(row.DrawingNumber, pdf.FileName))
              || _parts.Any(part => ExcelOrderQuantityService.MatchesDrawing(row.DrawingNumber, part.DrawingName ?? string.Empty))))
        continue;

      var matchIndex = FindMatchingBomIndex(row, bom, bomUsed, existingPdfs);
      AssemblyTubePosition? match = matchIndex >= 0 ? bom[matchIndex] : null;
      if (matchIndex >= 0)
        bomUsed[matchIndex] = true;

      var profile = row.Profile ?? match?.Profile;
      if (_cutProfile is not null
          && profile is not null
          && !string.Equals(_cutProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
      {
        notes.Add(
          ExcelRowLabel(row) + $": {profile.FullLabel} – anderes Maß als {_cutProfile.FullLabel}");
        continue;
      }

      var length = row.LengthMm ?? match?.LengthMm;
      var drawingNumber = !string.IsNullOrWhiteSpace(row.DrawingNumber)
        ? row.DrawingNumber
        : match?.Position is int pos
          ? "RZO-POS-" + pos
          : row.LineNumber is int line
            ? "RZO-POS-" + line
            : "RZO-" + sequence.ToString("000");
      sequence++;

      var description = !string.IsNullOrWhiteSpace(row.Description)
        ? row.Description
        : match?.Description ?? "TUBE";

      var positionLabel = match?.Position is int assemblyPos
        ? assemblyPos.ToString("0")
        : row.LineNumber is int excelLine
          ? "Excel " + excelLine
          : null;

      var sourceNote = match is null
        ? "Länge und Position ggf. in der Baugruppenzeichnung prüfen."
        : "Baugruppe " + match.SourceFileName
          + (match.Position is int p ? ", Position " + p : string.Empty)
          + (match.LengthMm is > 0 ? $", Länge {match.LengthMm:0.##} mm" : string.Empty)
          + ".";

      try
      {
        var pdfPath = WorkshopTubeDrawingService.Create(
          Path.Combine(_pdfImportRoot, "Werkstattzeichnungen"),
          drawingNumber,
          description,
          profile,
          length ?? 0,
          row.Quantity,
          string.IsNullOrWhiteSpace(orderReference) ? null : orderReference,
          sourceNote,
          positionLabel,
          match?.SourceFileName);

        var fileName = Path.GetFileName(pdfPath);
        var option = new PdfFileOption { FileName = fileName, FullPath = pdfPath };
        files.Add(option);
        existingPdfs.Add(option);

        var part = new CutPartEntry
        {
          DrawingName = fileName,
          PdfPath = pdfPath,
          LengthMm = length ?? 0,
          Quantity = row.Quantity
        };

        var existing = _parts.FirstOrDefault(entry =>
          string.Equals(entry.PdfPath ?? string.Empty, part.PdfPath, StringComparison.OrdinalIgnoreCase)
          || string.Equals(entry.DrawingName ?? string.Empty, part.DrawingName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
          var index = _parts.IndexOf(existing);
          if (index >= 0)
            _parts[index] = part;
        }
        else
        {
          _parts.Add(part);
        }

        generated++;
        quantityFromExcel++;

        if (length is not > 0)
          notes.Add($"{fileName}: Werkstattzeichnung erzeugt – Rohrlänge in der Baugruppe prüfen und ggf. eintragen.");
        else
          notes.Add($"{fileName}: aus Excel ohne Original-PDF erzeugt ({length:0.##} mm, {row.Quantity} Stück).");

        if (profile is not null)
        {
          TryApplyDetectedProfile(
            new PdfDrawingAnalysisResult
            {
              Profile = profile,
              Material = _cutMaterial ?? PipeMaterialTypes.Steel,
              Kind = DrawingPartKind.Pipe
            },
            overwriteExisting: _cutProfile is null,
            notes);
        }
      }
      catch (Exception ex)
      {
        notes.Add(ExcelRowLabel(row) + ": Werkstattzeichnung fehlgeschlagen – " + ex.Message);
      }
    }

    return generated;
  }

  private static int FindMatchingBomIndex(
    ExcelOrderQuantityRow row,
    IReadOnlyList<AssemblyTubePosition> bom,
    IReadOnlyList<bool> bomUsed,
    IReadOnlyList<PdfFileOption> existingPdfs)
  {
    if (!string.IsNullOrWhiteSpace(row.DrawingNumber))
    {
      for (var i = 0; i < bom.Count; i++)
      {
        if (bomUsed[i] || string.IsNullOrWhiteSpace(bom[i].DrawingNumber))
          continue;

        if (ExcelOrderQuantityService.MatchesDrawing(row.DrawingNumber, bom[i].DrawingNumber!))
          return i;
      }

      return -1;
    }

    var bestIndex = -1;
    var bestScore = -1;
    for (var i = 0; i < bom.Count; i++)
    {
      if (bomUsed[i])
        continue;

      var candidate = bom[i];
      if (!string.IsNullOrWhiteSpace(candidate.DrawingNumber)
          && existingPdfs.Any(pdf => ExcelOrderQuantityService.MatchesDrawing(candidate.DrawingNumber!, pdf.FileName)))
        continue;

      var score = 0;
      if (row.Profile is not null && candidate.Profile is not null)
      {
        if (!string.Equals(row.Profile.Id, candidate.Profile.Id, StringComparison.OrdinalIgnoreCase))
          continue;

        score += 50;
      }
      else
      {
        score += 10;
      }

      if (candidate.Quantity == row.Quantity)
        score += 20;

      if (string.IsNullOrWhiteSpace(candidate.DrawingNumber))
        score += 15;

      if (score > bestScore)
      {
        bestScore = score;
        bestIndex = i;
      }
    }

    return bestScore >= 10 ? bestIndex : -1;
  }

  private static string ExcelRowLabel(ExcelOrderQuantityRow row)
  {
    if (row.LineNumber is int line)
      return "Excel Zeile " + line;
    if (!string.IsNullOrWhiteSpace(row.Description))
      return row.Description;
    return "Excel-Rohr";
  }

  private List<string> CollectPdfPathsFromImport(IEnumerable<string> sourcePaths)
  {
    var importRoot = Path.Combine(AppInfo.UserDataDirectory, "Zeichnungen", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(importRoot);
    _pdfImportRoot = importRoot;

    var pdfPaths = new List<string>();
    foreach (var sourcePath in sourcePaths.Where(File.Exists))
    {
      var extension = Path.GetExtension(sourcePath);
      if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
      {
        var destination = Path.Combine(importRoot, Path.GetFileName(sourcePath));
        destination = MakeUniquePath(destination);
        File.Copy(sourcePath, destination, overwrite: false);
        pdfPaths.Add(destination);
        continue;
      }

      if (IsExcelImportExtension(extension))
      {
        var destination = Path.Combine(importRoot, Path.GetFileName(sourcePath));
        destination = MakeUniquePath(destination);
        File.Copy(sourcePath, destination, overwrite: false);
        continue;
      }

      if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        continue;

      var zipExtractRoot = Path.Combine(importRoot, Path.GetFileNameWithoutExtension(sourcePath));
      Directory.CreateDirectory(zipExtractRoot);
      ZipFile.ExtractToDirectory(sourcePath, zipExtractRoot, overwriteFiles: true);
      pdfPaths.AddRange(Directory.EnumerateFiles(zipExtractRoot, "*.pdf", SearchOption.AllDirectories));
    }

    return pdfPaths
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static string MakeUniquePath(string path)
  {
    if (!File.Exists(path))
      return path;

    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var name = Path.GetFileNameWithoutExtension(path);
    var extension = Path.GetExtension(path);
    for (var index = 2; index < 1000; index++)
    {
      var candidate = Path.Combine(directory, $"{name}_{index}{extension}");
      if (!File.Exists(candidate))
        return candidate;
    }

    return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
  }

  private void PdfComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (PdfComboBox.SelectedItem is not PdfFileOption pdf)
      return;

    try
    {
      var result = PdfDrawingAnalysisService.Analyze(pdf.FullPath);
      AnalysisTextBlock.Text = result.Summary;

      if (result.IsPipe)
      {
        if (result.LengthMm is > 0)
          PartLengthTextBox.Text = FormatInputNumber(result.LengthMm.Value);

        MiterEnd1TextBox.Text = FormatInputNumber(result.MiterEnd1Deg ?? 0);
        MiterEnd2TextBox.Text = FormatInputNumber(result.MiterEnd2Deg ?? 0);
        TryApplyDetectedProfile(result, overwriteExisting: _cutProfile is null);
      }
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
    AnalysisTextBlock.Text = "PDF oder ZIP ablegen – Länge und Gehrung werden erkannt und in die Teilliste übernommen.";
  }

  private void ClearResult()
  {
    _lastResult = null;
    _lastParts = [];
    SummaryTextBlock.Text = "Noch keine Berechnung. Nach Schritt 3 erscheint hier der Schnittplan.";
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
        "Bitte in Schritt 1 eine Auftragsnummer eingeben.",
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
    var folder = AppInfo.UserDataDirectory;
    Directory.CreateDirectory(folder);
    var suffix = string.IsNullOrWhiteSpace(_lastOrderReference)
      ? DateTime.Now.ToString("yyyyMMdd_HHmmss")
      : SanitizeFileNamePart(_lastOrderReference);
    return Path.Combine(folder, $"Zuschnittplan_{suffix}.pdf");
  }

  private static string CreateAutoOrderPdfPath(string orderReference)
  {
    var folder = AppInfo.UserDataDirectory;
    Directory.CreateDirectory(folder);
    return Path.Combine(folder, $"Bestellliste_{SanitizeFileNamePart(orderReference)}.pdf");
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

      var appSettings = AppSettingsStore.Load();
      var stockLengthMm = appSettings.StockLengthMm;
      if (stockLengthMm <= 0)
        throw new InvalidOperationException("Originalstange in den Einstellungen ist ungültig.");
      var kerfMm = appSettings.KerfMm;
      if (kerfMm < 0)
        throw new InvalidOperationException("Schnittbreite in den Einstellungen ist ungültig.");
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

      RefreshPartLengthsFromPdfs(parts);

      var preview = new OptimizePreviewWindow(
        orderReference,
        profile.FullLabel,
        _cutMaterial!,
        stockLengthMm,
        parts)
      {
        Owner = this
      };

      if (preview.ShowDialog() != true || !preview.Confirmed)
        return;

      var oversized = parts.Where(part => part.LengthMm > stockLengthMm).ToList();
      if (oversized.Count > 0)
      {
        MessageBox.Show(
          this,
          "Diese Teile sind länger als die Originalstange ("
          + stockLengthMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm):"
          + Environment.NewLine + Environment.NewLine
          + string.Join(Environment.NewLine, oversized.Select(part =>
            "• " + (string.IsNullOrWhiteSpace(part.DrawingName) ? "Manuelle Eingabe" : part.DrawingName)
            + ": " + part.LengthMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm × " + part.Quantity)),
          "Teil zu lang",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
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
      SummaryTextBlock.Text =
        SummaryTextBlock.Text
        + Environment.NewLine
        + "Zuschnittplan berechnet – PDF wurde geöffnet. Bei Bedarf rechts „Zuschnittplan speichern unter …“.";
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Berechnung fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private static void RefreshPartLengthsFromPdfs(List<CutPartEntry> parts)
  {
    foreach (var part in parts)
    {
      if (string.IsNullOrWhiteSpace(part.PdfPath) || !File.Exists(part.PdfPath))
        continue;

      try
      {
        var analysis = PdfDrawingAnalysisService.Analyze(part.PdfPath);
        if (analysis.LengthMm is > 0)
          part.LengthMm = analysis.LengthMm.Value;
        if (analysis.MiterEnd1Deg is not null)
          part.MiterEnd1Deg = MiterNotation.NormalizeInputAngle(analysis.MiterEnd1Deg.Value);
        if (analysis.MiterEnd2Deg is not null)
          part.MiterEnd2Deg = MiterNotation.NormalizeInputAngle(analysis.MiterEnd2Deg.Value);
      }
      catch
      {
        // bestehende Werte behalten
      }
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
