using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

public static class PipeStockCatalog
{
  public static IReadOnlyList<PipeProfileDefinition> All { get; } = BuildCatalog();

  public static PipeProfileDefinition? TryGet(string profileId) =>
    All.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));

  public static IEnumerable<PipeProfileDefinition> GetByKind(PipeProfileKind kind) =>
    All.Where(profile => profile.Kind == kind).OrderBy(profile => profile.Dimensions, StringComparer.OrdinalIgnoreCase);

  public static IEnumerable<double> GetRectWidths() =>
    GetByKind(PipeProfileKind.Rectangular)
      .Select(ParseRectSize)
      .Where(size => size is not null)
      .Select(size => size!.Value.Width)
      .Distinct()
      .OrderBy(value => value);

  public static IEnumerable<double> GetRectHeightsForWidth(double widthMm) =>
    GetByKind(PipeProfileKind.Rectangular)
      .Select(profile => (Profile: profile, Size: ParseRectSize(profile)))
      .Where(entry => entry.Size is not null && Math.Abs(entry.Size.Value.Width - widthMm) < 0.01)
      .Select(entry => entry.Size!.Value.Height)
      .Distinct()
      .OrderBy(value => value);

  public static PipeProfileDefinition? TryGetRect(double widthMm, double heightMm) =>
    GetByKind(PipeProfileKind.Rectangular)
      .FirstOrDefault(profile =>
      {
        var size = ParseRectSize(profile);
        return size is not null
               && Math.Abs(size.Value.Width - widthMm) < 0.01
               && Math.Abs(size.Value.Height - heightMm) < 0.01;
      });

  private static (double Width, double Height)? ParseRectSize(PipeProfileDefinition profile)
  {
    var key = profile.Id;
    if (key.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
      key = key[2..];

    var parts = key.Split('x');
    if (parts.Length < 2)
      return null;

    if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture, out var width))
      return null;

    if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture, out var height))
      return null;

    return (width, height);
  }

  private static List<PipeProfileDefinition> BuildCatalog()
  {
    var list = new List<PipeProfileDefinition>();

    AddRound(list, "10x1", "Ø 10 × 1");
    AddRound(list, "12x1", "Ø 12 × 1");
    AddRound(list, "12x1.5", "Ø 12 × 1,5");
    AddRound(list, "16x1.5", "Ø 16 × 1,5");
    AddRound(list, "20x2", "Ø 20 × 2");
    AddRound(list, "25x2", "Ø 25 × 2");
    AddRound(list, "30x2", "Ø 30 × 2");
    AddRound(list, "33.7x2", "Ø 33,7 × 2");
    AddRound(list, "38x2", "Ø 38 × 2");
    AddRound(list, "40x2", "Ø 40 × 2");
    AddRound(list, "42.4x2.6", "Ø 42,4 × 2,6");
    AddRound(list, "48.3x2.6", "Ø 48,3 × 2,6");
    AddRound(list, "50x2", "Ø 50 × 2");
    AddRound(list, "50x3", "Ø 50 × 3");
    AddRound(list, "60x3", "Ø 60 × 3");
    AddRound(list, "60.3x3", "Ø 60,3 × 3");
    AddRound(list, "76.1x3", "Ø 76,1 × 3");
    AddRound(list, "80x3", "Ø 80 × 3");
    AddRound(list, "88.9x3.2", "Ø 88,9 × 3,2");
    AddRound(list, "100x4", "Ø 100 × 4");
    AddRound(list, "101.6x4", "Ø 101,6 × 4");
    AddRound(list, "114.3x4", "Ø 114,3 × 4");
    AddRound(list, "139.7x4", "Ø 139,7 × 4");
    AddRound(list, "168.3x5", "Ø 168,3 × 5");
    AddRound(list, "219.1x6", "Ø 219,1 × 6");

    AddSquare(list, "15x15x1.5", "15 × 15 × 1,5");
    AddSquare(list, "20x20x1.5", "20 × 20 × 1,5");
    AddSquare(list, "20x20x2", "20 × 20 × 2");
    AddSquare(list, "25x25x2", "25 × 25 × 2");
    AddSquare(list, "30x30x2", "30 × 30 × 2");
    AddSquare(list, "30x30x3", "30 × 30 × 3");
    AddSquare(list, "35x35x2", "35 × 35 × 2");
    AddSquare(list, "40x40x2", "40 × 40 × 2");
    AddSquare(list, "40x40x3", "40 × 40 × 3");
    AddSquare(list, "50x50x2", "50 × 50 × 2");
    AddSquare(list, "50x50x3", "50 × 50 × 3");
    AddSquare(list, "60x60x2", "60 × 60 × 2");
    AddSquare(list, "60x60x3", "60 × 60 × 3");
    AddSquare(list, "60x60x4", "60 × 60 × 4");
    AddSquare(list, "70x70x3", "70 × 70 × 3");
    AddSquare(list, "80x80x3", "80 × 80 × 3");
    AddSquare(list, "80x80x4", "80 × 80 × 4");
    AddSquare(list, "100x100x4", "100 × 100 × 4");
    AddSquare(list, "100x100x5", "100 × 100 × 5");
    AddSquare(list, "120x120x5", "120 × 120 × 5");
    AddSquare(list, "150x150x6", "150 × 150 × 6");

    AddRect(list, "20x10x1.5", "20 × 10 × 1,5");
    AddRect(list, "30x15x2", "30 × 15 × 2");
    AddRect(list, "30x20x2", "30 × 20 × 2");
    AddRect(list, "40x20x2", "40 × 20 × 2");
    AddRect(list, "40x25x2", "40 × 25 × 2");
    AddRect(list, "50x25x2", "50 × 25 × 2");
    AddRect(list, "50x30x3", "50 × 30 × 3");
    AddRect(list, "60x30x3", "60 × 30 × 3");
    AddRect(list, "60x40x3", "60 × 40 × 3");
    AddRect(list, "80x40x3", "80 × 40 × 3");
    AddRect(list, "80x50x4", "80 × 50 × 4");
    AddRect(list, "100x50x4", "100 × 50 × 4");
    AddRect(list, "100x60x5", "100 × 60 × 5");
    AddRect(list, "120x60x5", "120 × 60 × 5");
    AddRect(list, "120x80x5", "120 × 80 × 5");
    AddRect(list, "150x100x6", "150 × 100 × 6");

    return list;
  }

  private static void AddRound(List<PipeProfileDefinition> list, string key, string dimensions) =>
    list.Add(Create(PipeProfileKind.Round, $"R-{key}", dimensions));

  private static void AddSquare(List<PipeProfileDefinition> list, string key, string dimensions) =>
    list.Add(Create(PipeProfileKind.Square, $"Q-{key}", dimensions));

  private static void AddRect(List<PipeProfileDefinition> list, string key, string dimensions) =>
    list.Add(Create(PipeProfileKind.Rectangular, $"P-{key}", dimensions));

  private static PipeProfileDefinition Create(PipeProfileKind kind, string id, string dimensions) =>
    new()
    {
      Id = id,
      Kind = kind,
      Dimensions = dimensions,
      DisplayName = dimensions
    };
}
