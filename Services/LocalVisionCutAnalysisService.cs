using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal sealed class LocalVisionCutReading
{
  public double? LengthMm { get; init; }
  public double? MiterEnd1Deg { get; init; }
  public double? MiterEnd2Deg { get; init; }
  public double? Confidence { get; init; }
  public string? Note { get; init; }
  public bool UsedLocalAi { get; init; }
}

/// <summary>
/// Echte lokale Vision-KI (mitgeliefertes Ollama + Modell unter AI\).
/// Zeichnungen nur an 127.0.0.1 – keine Cloud, keine separate Installation.
/// </summary>
internal static class LocalVisionCutAnalysisService
{
  private static readonly HttpClient Http = CreateClient();
  private static readonly Regex JsonObjectRegex = new(
    @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!))\}",
    RegexOptions.Singleline | RegexOptions.Compiled);

  public static async Task<LocalVisionCutReading?> TryEnrichAsync(
    string pdfPath,
    PdfDrawingAnalysisResult analysis,
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    if (!settings.LocalAiEnabled)
      return null;

    if (!analysis.IsPipe)
      return null;

    var needsLength = analysis.LengthMm is not > 0;
    var needsMiter = (analysis.MiterEnd1Deg is null or <= 0.1)
                     && (analysis.MiterEnd2Deg is null or <= 0.1);
    if (!needsLength && !needsMiter)
      return null;

    var (ready, readyMessage) = await BundledAiRuntime.EnsureReadyAsync(settings, cancellationToken)
      .ConfigureAwait(false);
    if (!ready)
      return new LocalVisionCutReading { Note = readyMessage, UsedLocalAi = false };

    if (!BundledAiRuntime.TryValidateLocalUrl(
          BundledAiRuntime.ResolveBaseUrl(settings),
          out var baseUri,
          out var endpointError))
      return new LocalVisionCutReading { Note = endpointError, UsedLocalAi = false };

    if (!PdfPreviewService.TryRenderFirstPagePng(pdfPath, out var pngBytes, out var renderError, dpi: 120)
        || pngBytes is null
        || pngBytes.Length == 0)
    {
      return new LocalVisionCutReading
      {
        Note = "Vision-KI: " + renderError,
        UsedLocalAi = false
      };
    }

    try
    {
      var model = BundledAiRuntime.ResolveModelName(settings);
      var prompt =
        "You analyze a technical pipe/tube engineering drawing image. "
        + "Reply ONLY with JSON, no markdown: "
        + "{\"lengthMm\":numberOrNull,\"miterEnd1Deg\":numberOrNull,\"miterEnd2Deg\":numberOrNull,\"confidence\":0to1}. "
        + "lengthMm = overall cut length of the tube in millimeters (NOT section size like 50x50x3). "
        + "miterEnd1Deg/miterEnd2Deg = end miter angles in degrees (0 or 90 means square cut). "
        + "If unsure use null.";

      var requestBody = new
      {
        model,
        stream = false,
        messages = new object[]
        {
          new
          {
            role = "user",
            content = prompt,
            images = new[] { Convert.ToBase64String(pngBytes) }
          }
        }
      };

      var json = JsonSerializer.Serialize(requestBody);
      using var content = new StringContent(json, Encoding.UTF8, "application/json");
      using var response = await Http.PostAsync(new Uri(baseUri!, "/api/chat"), content, cancellationToken)
        .ConfigureAwait(false);
      var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return new LocalVisionCutReading
        {
          Note = "Vision-KI Fehler (" + (int)response.StatusCode + ").",
          UsedLocalAi = false
        };
      }

      var modelText = ExtractMessageContent(responseText);
      if (string.IsNullOrWhiteSpace(modelText)
          || !TryParseVisionJson(modelText, analysis.Profile, out var lengthMm, out var miter1, out var miter2, out var confidence))
      {
        return new LocalVisionCutReading
        {
          Note = "Vision-KI: keine auswertbare Länge/Gehrung.",
          UsedLocalAi = true
        };
      }

      return new LocalVisionCutReading
      {
        LengthMm = needsLength ? lengthMm : null,
        MiterEnd1Deg = needsMiter ? miter1 : null,
        MiterEnd2Deg = needsMiter ? miter2 : null,
        Confidence = confidence,
        UsedLocalAi = true,
        Note = "Lokale Vision-KI"
      };
    }
    catch (TaskCanceledException)
    {
      return new LocalVisionCutReading
      {
        Note = "Vision-KI: Zeitüberschreitung (erstes Laden des Modells kann länger dauern).",
        UsedLocalAi = false
      };
    }
    catch (Exception ex)
    {
      return new LocalVisionCutReading
      {
        Note = "Vision-KI: " + ex.Message,
        UsedLocalAi = false
      };
    }
  }

  public static PdfDrawingAnalysisResult Merge(PdfDrawingAnalysisResult analysis, LocalVisionCutReading? ai)
  {
    if (ai is null || !ai.UsedLocalAi)
      return analysis;

    var length = analysis.LengthMm is > 0 ? analysis.LengthMm : ai.LengthMm;
    var miter1 = analysis.MiterEnd1Deg is > 0.1 ? analysis.MiterEnd1Deg : ai.MiterEnd1Deg;
    var miter2 = analysis.MiterEnd2Deg is > 0.1 ? analysis.MiterEnd2Deg : ai.MiterEnd2Deg;

    var summary = analysis.Summary;
    if (ai.LengthMm is > 0 && analysis.LengthMm is not > 0)
      summary += " · Rohrlänge per Vision-KI: "
                 + ai.LengthMm.Value.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))
                 + " mm";
    if ((ai.MiterEnd1Deg is > 0.1 || ai.MiterEnd2Deg is > 0.1)
        && analysis.MiterEnd1Deg is not > 0.1
        && analysis.MiterEnd2Deg is not > 0.1)
      summary += " · Gehrung per Vision-KI";

    return new PdfDrawingAnalysisResult
    {
      LengthMm = length,
      MiterEnd1Deg = miter1 is null ? null : MiterNotation.NormalizeInputAngle(miter1.Value),
      MiterEnd2Deg = miter2 is null ? null : MiterNotation.NormalizeInputAngle(miter2.Value),
      Profile = analysis.Profile,
      Material = analysis.Material,
      PartName = analysis.PartName,
      Kind = analysis.Kind,
      Summary = summary,
      LengthSource = length is > 0
        ? (analysis.LengthMm is > 0 ? analysis.LengthSource : AnalysisValueSource.LocalAi)
        : analysis.LengthSource,
      MiterSource = (miter1 is > 0.1 || miter2 is > 0.1)
        ? (analysis.MiterEnd1Deg is > 0.1 || analysis.MiterEnd2Deg is > 0.1
          ? analysis.MiterSource
          : AnalysisValueSource.LocalAi)
        : analysis.MiterSource
    };
  }

  public static async Task<(bool Ok, string Message)> ProbeAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default) =>
    await BundledAiRuntime.EnsureReadyAsync(settings, cancellationToken).ConfigureAwait(false);

  private static bool TryParseVisionJson(
    string modelText,
    PipeProfileDefinition? profile,
    out double? lengthMm,
    out double? miter1,
    out double? miter2,
    out double? confidence)
  {
    lengthMm = null;
    miter1 = null;
    miter2 = null;
    confidence = null;

    var json = modelText.Trim();
    var fenced = Regex.Match(json, @"```(?:json)?\s*(?<body>\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    if (fenced.Success)
      json = fenced.Groups["body"].Value;
    else
    {
      var objectMatch = JsonObjectRegex.Match(json);
      if (objectMatch.Success)
        json = objectMatch.Value;
      else
      {
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
          json = json[start..(end + 1)];
      }
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      lengthMm = ReadOptionalDouble(root, "lengthMm");
      miter1 = ReadOptionalDouble(root, "miterEnd1Deg");
      miter2 = ReadOptionalDouble(root, "miterEnd2Deg");
      confidence = ReadOptionalDouble(root, "confidence");

      if (lengthMm is double length && !PipePartParser.IsPlausibleLength(length, profile))
        lengthMm = null;
      if (miter1 is double a && a is < 0 or > 90.01)
        miter1 = null;
      if (miter2 is double b && b is < 0 or > 90.01)
        miter2 = null;

      return lengthMm is not null || miter1 is not null || miter2 is not null;
    }
    catch
    {
      return false;
    }
  }

  private static double? ReadOptionalDouble(JsonElement root, string name)
  {
    if (!root.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
      return null;
    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var number))
      return number;
    if (prop.ValueKind == JsonValueKind.String
        && double.TryParse(prop.GetString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
      return parsed;
    return null;
  }

  private static string? ExtractMessageContent(string responseJson)
  {
    try
    {
      using var doc = JsonDocument.Parse(responseJson);
      if (doc.RootElement.TryGetProperty("message", out var message)
          && message.TryGetProperty("content", out var content))
        return content.GetString();
      if (doc.RootElement.TryGetProperty("response", out var response))
        return response.GetString();
    }
    catch
    {
      return null;
    }

    return null;
  }

  private static HttpClient CreateClient()
  {
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RohreZuschnittOptimierung/VisionAi");
    return client;
  }
}
