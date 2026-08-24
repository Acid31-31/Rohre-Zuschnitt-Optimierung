using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class CompanionCadLengthService
{
  private static readonly Regex CartesianPointRegex = new(
    @"CARTESIAN_POINT\s*\(\s*'[^']*'\s*,\s*\(\s*([^)]+)\)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  public static double? TryGetLengthMm(string drawingPath, PipeProfileDefinition? profile)
  {
    var stepPath = FindCompanion(drawingPath, [".step", ".stp"]);
    if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
      return null;

    if (!TryGetBoundingBoxMm(stepPath, out var dx, out var dy, out var dz))
      return null;

    return PickLength(dx, dy, dz, profile);
  }

  private static string? FindCompanion(string drawingPath, IReadOnlyList<string> extensions)
  {
    var directory = Path.GetDirectoryName(drawingPath);
    var stem = Path.GetFileNameWithoutExtension(drawingPath);
    if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem) || !Directory.Exists(directory))
      return null;

    foreach (var file in Directory.EnumerateFiles(directory, stem + ".*"))
    {
      var ext = Path.GetExtension(file);
      if (extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
        return file;
    }

    return null;
  }

  private static bool TryGetBoundingBoxMm(string stepPath, out double dx, out double dy, out double dz)
  {
    dx = 0;
    dy = 0;
    dz = 0;

    string text;
    try
    {
      text = File.ReadAllText(stepPath);
    }
    catch
    {
      return false;
    }

    var minX = double.PositiveInfinity;
    var minY = double.PositiveInfinity;
    var minZ = double.PositiveInfinity;
    var maxX = double.NegativeInfinity;
    var maxY = double.NegativeInfinity;
    var maxZ = double.NegativeInfinity;
    var count = 0;

    foreach (Match match in CartesianPointRegex.Matches(text))
    {
      var parts = match.Groups[1].Value.Split(',');
      if (parts.Length < 3)
        continue;

      if (!TryParse(parts[0], out var x) || !TryParse(parts[1], out var y) || !TryParse(parts[2], out var z))
        continue;

      count++;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
      if (z < minZ) minZ = z;
      if (z > maxZ) maxZ = z;
    }

    if (count < 8 || double.IsInfinity(minX) || double.IsInfinity(maxX))
      return false;

    dx = maxX - minX;
    dy = maxY - minY;
    dz = maxZ - minZ;
    return dx > 0.5 && dy > 0.5 && dz > 0.5;
  }

  private static double? PickLength(double dx, double dy, double dz, PipeProfileDefinition? profile)
  {
    var extents = new[] { dx, dy, dz };

    if (profile is not null
        && PipeStockCatalog.TryParseCatalogSize(profile, out var primary, out var secondary, out _))
    {
      var section = Math.Max(primary, secondary);
      var along = extents.Where(value => value > section + 1.5).ToList();
      if (along.Count == 1)
        return RoundWorkshop(along[0]);
      if (along.Count > 1)
        return RoundWorkshop(along.Max());
    }

    var longest = extents.Max();
    if (longest is >= 20 and <= 12000)
      return RoundWorkshop(longest);

    return null;
  }

  private static double RoundWorkshop(double mm)
  {
    var tenth = Math.Round(mm, 1, MidpointRounding.AwayFromZero);
    if (Math.Abs(tenth - Math.Round(tenth)) < 0.05)
      return Math.Round(tenth);
    return tenth;
  }

  private static bool TryParse(string raw, out double value) =>
    double.TryParse(
      raw.Trim(),
      NumberStyles.Float,
      CultureInfo.InvariantCulture,
      out value);
}
