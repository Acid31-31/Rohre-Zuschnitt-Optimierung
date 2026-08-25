using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public sealed class PartsCapturePrefill
{
  public double? LengthMm { get; init; }
  public int? Quantity { get; init; }
  public double? MiterEnd1Deg { get; init; }
  public double? MiterEnd2Deg { get; init; }
  public string? DrawingName { get; init; }
  public string? PdfPath { get; init; }
}

public partial class PartsCaptureWindow : Window
{
  private readonly ObservableCollection<CutPartEntry> _parts;
  private readonly Action<PipeProfileDefinition, string> _applyProfile;
  private PipeProfileDefinition? _profile;
  private string? _material;
  private CutPartEntry? _editingPart;
  private string? _pendingDrawingName;
  private string? _pendingPdfPath;

  public PartsCaptureWindow(
    ObservableCollection<CutPartEntry> parts,
    PipeProfileDefinition? profile,
    string? material,
    Action<PipeProfileDefinition, string> applyProfile,
    CutPartEntry? editPart = null,
    PartsCapturePrefill? prefill = null)
  {
    InitializeComponent();
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);

    _parts = parts;
    _applyProfile = applyProfile;
    _profile = profile;
    _material = material;
    PartsGrid.ItemsSource = _parts;
    _parts.CollectionChanged += Parts_CollectionChanged;

    UpdateProfileDisplay();
    UpdatePartsCount();
    ResetInputs();

    if (editPart is not null)
      BeginEdit(editPart);
    else if (prefill is not null)
      ApplyPrefill(prefill);

    Loaded += (_, _) => PartLengthTextBox.Focus();
  }

  protected override void OnClosed(EventArgs e)
  {
    _parts.CollectionChanged -= Parts_CollectionChanged;
    base.OnClosed(e);
  }

  private void Parts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
    UpdatePartsCount();

  private void UpdatePartsCount()
  {
    var pieces = _parts.Sum(part => part.Quantity);
    PartsCountTextBlock.Text = _parts.Count == 0
      ? "Noch keine Teile"
      : $"{_parts.Count} Position(en) · {pieces} Stück gesamt";
  }

  private void UpdateProfileDisplay()
  {
    if (_profile is null || string.IsNullOrWhiteSpace(_material))
    {
      ProfileDisplayTextBlock.Text = "Noch kein Profil gewählt";
      SelectProfileButton.Content = "Profil wählen …";
      return;
    }

    ProfileDisplayTextBlock.Text = $"{_profile.FullLabel} · {_material}";
    SelectProfileButton.Content = "Profil ändern …";
  }

  private void SelectProfile_Click(object sender, RoutedEventArgs e)
  {
    var wizard = new PipeWarehouseAddWizardWindow(WarehouseWizardMode.SelectCutProfile) { Owner = this };
    if (wizard.ShowDialog() != true
        || wizard.Result?.Profile is null
        || string.IsNullOrWhiteSpace(wizard.Result.Material))
      return;

    _profile = wizard.Result.Profile;
    _material = wizard.Result.Material;
    _applyProfile(_profile, _material);
    UpdateProfileDisplay();
  }

  private void AddPart_Click(object sender, RoutedEventArgs e) => TryCommitPart();

  private bool TryCommitPart()
  {
    try
    {
      var part = BuildPartFromInputs();

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
        ResetInputs();
        PartLengthTextBox.Focus();
        return true;
      }

      _parts.Add(part);
      ResetInputs();
      PartLengthTextBox.Focus();
      return true;
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Eingabe unvollständig", MessageBoxButton.OK, MessageBoxImage.Warning);
      return false;
    }
  }

  private void EditSelected_Click(object sender, RoutedEventArgs e)
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

    BeginEdit(selected);
  }

  private void PartsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (PartsGrid.SelectedItem is CutPartEntry selected)
      BeginEdit(selected);
  }

  private void RemoveSelected_Click(object sender, RoutedEventArgs e)
  {
    if (PartsGrid.SelectedItem is not CutPartEntry selected)
      return;

    if (_editingPart == selected)
      ClearEditingMode();

    _parts.Remove(selected);
  }

  private void BeginEdit(CutPartEntry selected)
  {
    _editingPart = selected;
    PartLengthTextBox.Text = FormatInputNumber(selected.LengthMm);
    PartQuantityTextBox.Text = selected.Quantity.ToString(CultureInfo.InvariantCulture);
    MiterEnd1TextBox.Text = FormatInputNumber(selected.MiterEnd1Deg);
    MiterEnd2TextBox.Text = FormatInputNumber(selected.MiterEnd2Deg);
    _pendingDrawingName = selected.DrawingName;
    _pendingPdfPath = selected.PdfPath;
    UpdateAddButtonLabel();
    PartLengthTextBox.Focus();
    PartLengthTextBox.SelectAll();
  }

  private void ApplyPrefill(PartsCapturePrefill prefill)
  {
    if (prefill.LengthMm is > 0)
      PartLengthTextBox.Text = FormatInputNumber(prefill.LengthMm.Value);
    if (prefill.Quantity is > 0)
      PartQuantityTextBox.Text = prefill.Quantity.Value.ToString(CultureInfo.InvariantCulture);
    if (prefill.MiterEnd1Deg is not null)
      MiterEnd1TextBox.Text = FormatInputNumber(prefill.MiterEnd1Deg.Value);
    if (prefill.MiterEnd2Deg is not null)
      MiterEnd2TextBox.Text = FormatInputNumber(prefill.MiterEnd2Deg.Value);
    _pendingDrawingName = prefill.DrawingName;
    _pendingPdfPath = prefill.PdfPath;
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

  private void ResetInputs()
  {
    PartLengthTextBox.Clear();
    PartQuantityTextBox.Text = "1";
    MiterEnd1TextBox.Text = "0";
    MiterEnd2TextBox.Text = "0";
    _pendingDrawingName = null;
    _pendingPdfPath = null;
    ClearEditingMode();
  }

  private CutPartEntry BuildPartFromInputs()
  {
    var lengthMm = ParsePositiveDouble(PartLengthTextBox.Text, "Rohrlänge");
    var quantity = ParsePositiveInt(PartQuantityTextBox.Text, "Stückzahl");
    var miterEnd1 = MiterNotation.NormalizeInputAngle(
      ParseNonNegativeDouble(MiterEnd1TextBox.Text, "Gehrung Ende A"));
    var miterEnd2 = MiterNotation.NormalizeInputAngle(
      ParseNonNegativeDouble(MiterEnd2TextBox.Text, "Gehrung Ende B"));

    return new CutPartEntry
    {
      DrawingName = _pendingDrawingName,
      PdfPath = _pendingPdfPath,
      LengthMm = lengthMm,
      MiterEnd1Deg = miterEnd1,
      MiterEnd2Deg = miterEnd2,
      Quantity = quantity
    };
  }

  private void Done_Click(object sender, RoutedEventArgs e) => Close();

  private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      Close();
      e.Handled = true;
      return;
    }

    if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
    {
      if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
      {
        TryCommitPart();
        e.Handled = true;
      }
    }
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
    if (string.IsNullOrWhiteSpace(text))
      return 0;

    if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        || value < 0)
      throw new InvalidOperationException($"{fieldName} muss eine Zahl ≥ 0 sein.");
    return value;
  }

  private static string FormatInputNumber(double value) =>
    value.ToString("0.##", CultureInfo.InvariantCulture);
}
