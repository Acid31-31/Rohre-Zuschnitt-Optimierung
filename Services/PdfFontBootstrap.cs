using System.IO;
using PdfSharp.Fonts;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// PDFsharp 6 benötigt unter .NET 8 einen Font-Resolver. Auf Windows werden System-Schriften geladen.
/// </summary>
public static class PdfFontBootstrap
{
  private static bool _initialized;

  public static void EnsureInitialized()
  {
    if (_initialized)
      return;

    GlobalFontSettings.FontResolver = new WindowsPdfFontResolver();
    _initialized = true;
  }
}

internal sealed class WindowsPdfFontResolver : IFontResolver
{
  private static readonly string FontsFolder =
    Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

  private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

  public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
  {
    var platform = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
    if (platform is not null)
      return platform;

    var fileName = ResolveFontFile(familyName, isBold, isItalic)
                   ?? ResolveFontFile("Arial", isBold, isItalic);
    if (fileName is null)
      return null;

    var faceName = $"file:{fileName}";
    return new FontResolverInfo(faceName, false, isItalic);
  }

  public byte[]? GetFont(string faceName)
  {
    if (!faceName.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
      return null;

    if (_cache.TryGetValue(faceName, out var cached))
      return cached;

    var fileName = faceName["file:".Length..];
    var path = Path.Combine(FontsFolder, fileName);
    if (!File.Exists(path))
      return null;

    var bytes = File.ReadAllBytes(path);
    _cache[faceName] = bytes;
    return bytes;
  }

  private static string? ResolveFontFile(string familyName, bool isBold, bool isItalic)
  {
    if (familyName.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
    {
      if (isBold && isItalic)
        return "segoeuiz.ttf";
      if (isBold)
        return "segoeuib.ttf";
      if (isItalic)
        return "segoeuii.ttf";
      return "segoeui.ttf";
    }

    if (familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase))
    {
      if (isBold && isItalic)
        return "arialbi.ttf";
      if (isBold)
        return "arialbd.ttf";
      if (isItalic)
        return "ariali.ttf";
      return "arial.ttf";
    }

    return null;
  }
}
