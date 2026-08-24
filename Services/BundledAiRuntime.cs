using System.Diagnostics;
using System.IO;
using System.Net.Http;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Startet die mitgelieferte lokale Vision-KI (Ollama portable) aus dem Programmordner AI\.
/// Keine separate Installation, Zeichnungen bleiben auf localhost.
/// </summary>
internal static class BundledAiRuntime
{
  private static readonly object Gate = new();
  private static Process? _serveProcess;
  private static readonly HttpClient ProbeHttp = CreateProbeClient();

  public const string DefaultModel = "moondream";
  public const string BundledBaseUrl = "http://127.0.0.1:11435";

  public static string GetAiRoot() =>
    Path.Combine(AppInfo.GetApplicationDirectory(), "AI");

  public static string GetOllamaExePath() =>
    Path.Combine(GetAiRoot(), "ollama", "ollama.exe");

  public static string GetModelsPath() =>
    Path.Combine(GetAiRoot(), "models");

  public static bool IsBundled => File.Exists(GetOllamaExePath());

  public static string ResolveBaseUrl(AppSettings settings) =>
    IsBundled
      ? BundledBaseUrl
      : (string.IsNullOrWhiteSpace(settings.OllamaBaseUrl)
        ? BundledBaseUrl
        : settings.OllamaBaseUrl.Trim());

  public static async Task<(bool Ok, string Message)> EnsureReadyAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    if (!IsBundled)
    {
      return (false,
        "Lokale KI fehlt im Programmordner (AI\\ollama). Bitte R18/USB-Version mit KI-Paket verwenden.");
    }

    if (!TryValidateLocalUrl(ResolveBaseUrl(settings), out var baseUri, out var urlError))
      return (false, urlError);

    if (await IsApiUpAsync(baseUri!, cancellationToken).ConfigureAwait(false))
    {
      var model = ResolveModelName(settings);
      if (await HasModelAsync(baseUri!, model, cancellationToken).ConfigureAwait(false))
        return (true, "Lokale Vision-KI bereit („" + model + "“). Zeichnungen bleiben auf dem PC.");

      return (false,
        "KI läuft, aber Modell „" + model + "“ fehlt unter AI\\models. USB-Paket vollständig kopieren.");
    }

    if (!TryStartServe(out var startError))
      return (false, startError);

    for (var attempt = 0; attempt < 40; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      await Task.Delay(500, cancellationToken).ConfigureAwait(false);
      if (await IsApiUpAsync(baseUri!, cancellationToken).ConfigureAwait(false))
      {
        var model = ResolveModelName(settings);
        if (await HasModelAsync(baseUri!, model, cancellationToken).ConfigureAwait(false))
          return (true, "Lokale Vision-KI gestartet („" + model + "“). Zeichnungen bleiben auf dem PC.");

        return (false,
          "KI gestartet, Modell „" + model + "“ fehlt. USB-Paket mit AI\\models verwenden.");
      }
    }

    return (false, "Lokale KI startet nicht rechtzeitig. Ordner AI\\ollama prüfen.");
  }

  public static string ResolveModelName(AppSettings settings) =>
    string.IsNullOrWhiteSpace(settings.OllamaVisionModel)
      ? DefaultModel
      : settings.OllamaVisionModel.Trim();

  public static bool TryValidateLocalUrl(string? configuredUrl, out Uri? baseUri, out string error)
  {
    baseUri = null;
    error = string.Empty;
    var raw = string.IsNullOrWhiteSpace(configuredUrl) ? BundledBaseUrl : configuredUrl.Trim();
    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
    {
      error = "Ungültige lokale KI-Adresse.";
      return false;
    }

    var host = uri.Host ?? string.Empty;
    if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && !host.Equals("::1", StringComparison.OrdinalIgnoreCase))
    {
      error = "Nur localhost erlaubt – Zeichnungen dürfen nicht online gehen.";
      return false;
    }

    baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority));
    return true;
  }

  private static bool TryStartServe(out string error)
  {
    error = string.Empty;
    lock (Gate)
    {
      if (_serveProcess is { HasExited: false })
        return true;

      try
      {
        Directory.CreateDirectory(GetModelsPath());
        var start = new ProcessStartInfo
        {
          FileName = GetOllamaExePath(),
          Arguments = "serve",
          WorkingDirectory = Path.GetDirectoryName(GetOllamaExePath()) ?? GetAiRoot(),
          UseShellExecute = false,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        };
        start.Environment["OLLAMA_MODELS"] = GetModelsPath();
        start.Environment["OLLAMA_HOST"] = "127.0.0.1:11435";

        _serveProcess = Process.Start(start);
        if (_serveProcess is null)
        {
          error = "Lokale KI konnte nicht gestartet werden.";
          return false;
        }

        return true;
      }
      catch (Exception ex)
      {
        error = "Lokale KI Startfehler: " + ex.Message;
        return false;
      }
    }
  }

  private static async Task<bool> IsApiUpAsync(Uri baseUri, CancellationToken cancellationToken)
  {
    try
    {
      using var response = await ProbeHttp.GetAsync(new Uri(baseUri, "/api/tags"), cancellationToken)
        .ConfigureAwait(false);
      return response.IsSuccessStatusCode;
    }
    catch
    {
      return false;
    }
  }

  private static async Task<bool> HasModelAsync(Uri baseUri, string model, CancellationToken cancellationToken)
  {
    try
    {
      using var response = await ProbeHttp.GetAsync(new Uri(baseUri, "/api/tags"), cancellationToken)
        .ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
        return false;

      var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      return body.Contains(model, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
    }
  }

  private static HttpClient CreateProbeClient()
  {
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RohreZuschnittOptimierung/BundledAi");
    return client;
  }
}
