using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class PipeWarehouseAddWizardWindow : Window
{
  private enum WizardStep
  {
    ProfileKind,
    Material,
    Dimension,
    RectHeight,
    Length,
    Quantity
  }

  private readonly WarehouseWizardMode _mode;
  private WizardStep _step = WizardStep.ProfileKind;
  private PipeProfileKind? _profileKind;
  private string? _material;
  private PipeProfileDefinition? _profile;
  private double? _rectWidthMm;
  private double? _lengthMm;

  public WarehouseAddWizardResult? Result { get; private set; }

  public PipeWarehouseAddWizardWindow(WarehouseWizardMode mode)
  {
    _mode = mode;
    InitializeComponent();
    Title = mode switch
    {
      WarehouseWizardMode.AddNewMaterial => "Neumaterial anlegen",
      WarehouseWizardMode.AddRemnant => "Rohrest anlegen",
      WarehouseWizardMode.SelectCutProfile => "Rohrprofil zum Schneiden",
      _ => "Auswahl"
    };
    TitleTextBlock.Text = Title;
    Loaded += (_, _) => WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
    ShowStep();
  }

  public PipeWarehouseAddWizardWindow(bool isNewMaterial)
    : this(isNewMaterial ? WarehouseWizardMode.AddNewMaterial : WarehouseWizardMode.AddRemnant)
  {
  }

  private bool IsSelectMode => _mode == WarehouseWizardMode.SelectCutProfile;
  private bool IsNewMaterial => _mode == WarehouseWizardMode.AddNewMaterial;

  private void ShowStep()
  {
    ChoiceButtonsPanel.Children.Clear();
    InputPanel.Visibility = Visibility.Collapsed;
    LengthLabel.Visibility = Visibility.Collapsed;
    LengthTextBox.Visibility = Visibility.Collapsed;
    NextButton.Content = "Weiter";
    NextButton.IsEnabled = false;
    NextButton.Visibility = IsSelectMode ? Visibility.Collapsed : Visibility.Visible;
    BackButton.IsEnabled = _step != WizardStep.ProfileKind;
    SummaryTextBlock.Text = BuildSummary();

    switch (_step)
    {
      case WizardStep.ProfileKind:
        StepTextBlock.Text = "Schritt 1: Rohrprofil wählen";
        AddChoiceButton("Rundrohr", () => SelectProfileKind(PipeProfileKind.Round));
        AddChoiceButton("Vierkantrohr", () => SelectProfileKind(PipeProfileKind.Square));
        AddChoiceButton("Rechteckrohr", () => SelectProfileKind(PipeProfileKind.Rectangular));
        break;

      case WizardStep.Material:
        StepTextBlock.Text = "Schritt 2: Materialart wählen";
        foreach (var material in PipeMaterialTypes.All)
          AddChoiceButton(material, () => SelectMaterial(material));
        break;

      case WizardStep.Dimension:
        StepTextBlock.Text = _profileKind == PipeProfileKind.Rectangular
          ? "Schritt 3: Profilbreite wählen"
          : "Schritt 3: Profilmaße wählen";
        ShowDimensionChoices();
        break;

      case WizardStep.RectHeight:
        StepTextBlock.Text = "Schritt 4: Profilhöhe wählen";
        foreach (var height in PipeStockCatalog.GetRectHeightsForWidth(_rectWidthMm!.Value))
          AddChoiceButton($"{height:0} mm", () => SelectRectHeight(height));
        break;

      case WizardStep.Length:
        StepTextBlock.Text = "Schritt 5: Restlänge eingeben";
        InputPanel.Visibility = Visibility.Visible;
        InputLabelTextBlock.Text = "Rohrest — Länge und Stückzahl";
        LengthLabel.Visibility = Visibility.Visible;
        LengthTextBox.Visibility = Visibility.Visible;
        LengthTextBox.Text = _lengthMm?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;
        NextButton.Content = "Weiter";
        NextButton.IsEnabled = true;
        NextButton.Visibility = Visibility.Visible;
        break;

      case WizardStep.Quantity:
        StepTextBlock.Text = IsNewMaterial
          ? "Schritt 4: Stückzahl für Neumaterial (6.000 mm)"
          : "Schritt 6: Stückzahl";
        InputPanel.Visibility = Visibility.Visible;
        InputLabelTextBlock.Text = "Stückzahl";
        NextButton.Content = "Fertig";
        NextButton.IsEnabled = true;
        NextButton.Visibility = Visibility.Visible;
        break;
    }
  }

  private void ShowDimensionChoices()
  {
    if (_profileKind is null)
      return;

    if (_profileKind == PipeProfileKind.Rectangular)
    {
      foreach (var width in PipeStockCatalog.GetRectWidths())
        AddChoiceButton($"{width:0} mm", () => SelectRectWidth(width));
      return;
    }

    foreach (var profile in PipeStockCatalog.GetByKind(_profileKind.Value))
      AddChoiceButton(profile.Dimensions, () => SelectProfile(profile));
  }

  private void AddChoiceButton(string label, Action onClick)
  {
    var button = new Button
    {
      Content = label,
      Padding = new Thickness(16, 10, 16, 10),
      Margin = new Thickness(0, 0, 10, 10),
      MinWidth = 120,
      FontSize = 14
    };
    button.Click += (_, _) => onClick();
    ChoiceButtonsPanel.Children.Add(button);
  }

  private void SelectProfileKind(PipeProfileKind kind)
  {
    _profileKind = kind;
    _profile = null;
    _rectWidthMm = null;
    GoNext();
  }

  private void SelectMaterial(string material)
  {
    _material = material;
    GoNext();
  }

  private void SelectProfile(PipeProfileDefinition profile)
  {
    _profile = profile;
    if (IsSelectMode)
    {
      CompleteSelection();
      return;
    }

    GoNext();
  }

  private void SelectRectWidth(double widthMm)
  {
    _rectWidthMm = widthMm;
    _profile = null;
    _step = WizardStep.RectHeight;
    ShowStep();
  }

  private void SelectRectHeight(double heightMm)
  {
    _profile = PipeStockCatalog.TryGetRect(_rectWidthMm!.Value, heightMm);
    if (_profile is null)
    {
      MessageBox.Show(this, "Profil nicht gefunden.", "Auswahl", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    if (IsSelectMode)
    {
      CompleteSelection();
      return;
    }

    GoNext();
  }

  private void CompleteSelection()
  {
    if (_profile is null || string.IsNullOrWhiteSpace(_material))
      return;

    Result = new WarehouseAddWizardResult
    {
      Profile = _profile,
      Material = _material,
      IsSelectionOnly = true
    };

    DialogResult = true;
    Close();
  }

  private void GoNext()
  {
    _step = GetNextStep(_step);
    ShowStep();
  }

  private void GoBack()
  {
    _step = GetPreviousStep(_step);
    ShowStep();
  }

  private WizardStep GetNextStep(WizardStep current) => current switch
  {
    WizardStep.ProfileKind => WizardStep.Material,
    WizardStep.Material => WizardStep.Dimension,
    WizardStep.Dimension when _profileKind == PipeProfileKind.Rectangular && _profile is null =>
      WizardStep.RectHeight,
    WizardStep.Dimension when IsSelectMode => WizardStep.Dimension,
    WizardStep.Dimension => IsNewMaterial ? WizardStep.Quantity : WizardStep.Length,
    WizardStep.RectHeight when IsSelectMode => WizardStep.RectHeight,
    WizardStep.RectHeight => IsNewMaterial ? WizardStep.Quantity : WizardStep.Length,
    WizardStep.Length => WizardStep.Quantity,
    _ => WizardStep.Quantity
  };

  private WizardStep GetPreviousStep(WizardStep current) => current switch
  {
    WizardStep.Material => WizardStep.ProfileKind,
    WizardStep.Dimension => WizardStep.Material,
    WizardStep.RectHeight => WizardStep.Dimension,
    WizardStep.Length => _profileKind == PipeProfileKind.Rectangular
      ? WizardStep.RectHeight
      : WizardStep.Dimension,
    WizardStep.Quantity when IsNewMaterial && _profileKind == PipeProfileKind.Rectangular =>
      WizardStep.RectHeight,
    WizardStep.Quantity when IsNewMaterial => WizardStep.Dimension,
    WizardStep.Quantity => WizardStep.Length,
    _ => WizardStep.ProfileKind
  };

  private string BuildSummary()
  {
    var parts = new List<string>();
    if (_profileKind is not null)
      parts.Add(_profileKind switch
      {
        PipeProfileKind.Round => "Rundrohr",
        PipeProfileKind.Square => "Vierkantrohr",
        PipeProfileKind.Rectangular => "Rechteckrohr",
        _ => string.Empty
      });
    if (!string.IsNullOrWhiteSpace(_material))
      parts.Add(_material);
    if (_rectWidthMm is not null && _profile is null)
      parts.Add($"Breite {_rectWidthMm:0} mm");
    if (_profile is not null)
      parts.Add(_profile.Dimensions);
    if (_lengthMm is > 0)
      parts.Add($"Länge {_lengthMm:0} mm");

    return parts.Count == 0 ? string.Empty : "Auswahl: " + string.Join(" · ", parts);
  }

  private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

  private void Next_Click(object sender, RoutedEventArgs e)
  {
    if (_step == WizardStep.Length)
    {
      try
      {
        _lengthMm = ParsePositiveDouble(LengthTextBox.Text, "Länge");
        if (Math.Abs(_lengthMm.Value - CutOptimizationDefaults.StockLengthMm) < 0.5)
        {
          MessageBox.Show(this, "Für 6.000 mm bitte „Neumaterial anlegen“ verwenden.", "Rohrest",
            MessageBoxButton.OK, MessageBoxImage.Information);
          return;
        }

        GoNext();
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, "Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
      }

      return;
    }

    if (_step == WizardStep.Quantity)
    {
      try
      {
        if (_profile is null || string.IsNullOrWhiteSpace(_material))
          throw new InvalidOperationException("Profil oder Material fehlt.");

        var quantity = ParsePositiveInt(QuantityTextBox.Text, "Stückzahl");
        var length = IsNewMaterial
          ? CutOptimizationDefaults.StockLengthMm
          : _lengthMm ?? throw new InvalidOperationException("Restlänge fehlt.");

        Result = new WarehouseAddWizardResult
        {
          Profile = _profile,
          Material = _material,
          LengthMm = length,
          Quantity = quantity,
          IsNewMaterial = IsNewMaterial
        };

        DialogResult = true;
        Close();
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, "Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
      }

      return;
    }

    GoNext();
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
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
}
