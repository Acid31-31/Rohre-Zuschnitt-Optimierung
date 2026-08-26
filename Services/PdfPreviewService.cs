using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class PdfPreviewService
{
  public static bool TryRenderFirstPage(string pdfPath, out BitmapSource? image, out string error, int dpi = 110)
  {
    image = null;
    error = string.Empty;

    if (!TryRenderFirstPagePng(pdfPath, out var pngBytes, out error, dpi))
      return false;

    try
    {
      using var stream = new MemoryStream(pngBytes!);
      var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
      var frame = decoder.Frames[0];
      frame.Freeze();
      image = frame;
      return true;
    }
    catch (Exception ex)
    {
      error = "PDF-Vorschau fehlgeschlagen: " + ex.Message;
      return false;
    }
  }

  public static bool TryRenderFirstPagePng(string pdfPath, out byte[]? pngBytes, out string error, int dpi = 120)
  {
    pngBytes = null;
    error = string.Empty;

    if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
    {
      error = "PDF-Datei nicht gefunden.";
      return false;
    }

    try
    {
      var scaling = Math.Max(0.5, dpi / 72.0);
      using var docReader = DocLib.Instance.GetDocReader(File.ReadAllBytes(pdfPath), new PageDimensions(scaling));
      using var pageReader = docReader.GetPageReader(0);
      var width = pageReader.GetPageWidth();
      var height = pageReader.GetPageHeight();
      if (width <= 0 || height <= 0)
      {
        error = "PDF-Seite hat ungültige Größe.";
        return false;
      }

      var raw = pageReader.GetImage();
      if (raw is null || raw.Length == 0)
      {
        error = "PDF-Seite konnte nicht gerendert werden.";
        return false;
      }

      // Pdfium liefert oft transparente Pixel; auf Weiß compositen = normale Papieransicht.
      FlattenPremultipliedBgraOntoWhite(raw);

      var bitmap = BitmapSource.Create(
        width,
        height,
        dpi,
        dpi,
        PixelFormats.Bgra32,
        null,
        raw,
        width * 4);
      bitmap.Freeze();

      var encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var stream = new MemoryStream();
      encoder.Save(stream);
      pngBytes = stream.ToArray();
      return pngBytes.Length > 0;
    }
    catch (DllNotFoundException)
    {
      error = "PDF-Vorschau nicht verfügbar (pdfium.dll fehlt).";
      return false;
    }
    catch (Exception ex)
    {
      error = "PDF-Vorschau fehlgeschlagen: " + ex.Message;
      return false;
    }
  }

  /// <summary>
  /// Docnet/Pdfium BGRA kann premultiplied + transparent sein. Blend auf Weiß für normale Darstellung.
  /// </summary>
  private static void FlattenPremultipliedBgraOntoWhite(byte[] bgra)
  {
    for (var i = 0; i + 3 < bgra.Length; i += 4)
    {
      var a = bgra[i + 3];
      if (a == 255)
        continue;

      if (a == 0)
      {
        bgra[i] = 255;
        bgra[i + 1] = 255;
        bgra[i + 2] = 255;
        bgra[i + 3] = 255;
        continue;
      }

      // Premultiplied: C_out = C_src + white * (1 - a/255)
      var inv = 255 - a;
      bgra[i] = (byte)Math.Min(255, bgra[i] + inv);
      bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] + inv);
      bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] + inv);
      bgra[i + 3] = 255;
    }
  }
}
