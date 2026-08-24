using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Windows;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class GitHubUpdateService
{
  private static readonly string UpdateRoot = Path.Combine(AppInfo.UserDataDirectory, "Updates");

  public static async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
  {
    var result = new AppUpdateInfo();
    AppSecurityService.EnsureSecureTransport();

    try
    {
      var json = await FetchLatestReleaseJsonAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();

      result.ReleaseTag = ExtractJsonString(json, "tag_name") ?? string.Empty;
      result.ReleaseNotes = ReleaseNotesFormatter.NormalizeReleaseNotes(
        ExtractJsonString(json, "body") ?? string.Empty);
      result.ExpectedSha256 = ExtractSha256FromReleaseNotes(result.ReleaseNotes);

      if (!TryParseReleaseVersion(result.ReleaseTag, out var remoteVersion))
      {
        result.ErrorMessage = "Release-Version konnte nicht gelesen werden: " + result.ReleaseTag;
        return result;
      }

      result.RemoteVersion = remoteVersion;
      result.UpdateAvailable = remoteVersion > AppInfo.ApplicationVersion;

      if (!result.UpdateAvailable)
        return result;

      if (string.IsNullOrWhiteSpace(result.ExpectedSha256))
      {
        result.ErrorMessage = "Release enthält keine SHA256-Prüfsumme. Update aus Sicherheitsgründen blockiert.";
        result.UpdateAvailable = false;
        return result;
      }

      var asset = ExtractPreferredAsset(json);
      if (asset is null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
      {
        result.UpdateAvailable = false;
        result.ErrorMessage = "Kein Update-Paket (" + AppInfo.UpdateAssetFileName + ") in der Release gefunden.";
        return result;
      }

      result.DownloadUrl = asset.DownloadUrl;
      result.AssetId = asset.AssetId;
      result.AssetName = asset.Name ?? AppInfo.UpdateAssetFileName;
    }
    catch (Exception ex)
    {
      result.ErrorMessage = DescribeUpdateError(ex);
    }

    return result;
  }

  public static async Task<string> DownloadAndStageUpdateAsync(
    AppUpdateInfo update,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken = default)
  {
    if (update.AssetId <= 0 && string.IsNullOrWhiteSpace(update.DownloadUrl))
      throw new InvalidOperationException("Kein Update-Download verfügbar.");

    Directory.CreateDirectory(UpdateRoot);
    var packagePath = Path.Combine(UpdateRoot, AppSecurityService.SanitizeFileName(update.AssetName));
    var extractPath = Path.Combine(UpdateRoot, "staging-" + DateTime.Now.ToString("yyyyMMddHHmmss"));

    if (Directory.Exists(extractPath))
      Directory.Delete(extractPath, true);

    ReportProgress(progress, 2, "Update wird heruntergeladen…");
    var errors = new List<string>();

    if (!string.IsNullOrWhiteSpace(update.DownloadUrl)
        && await TryDownloadBrowserAssetAsync(update.DownloadUrl, packagePath, errors, progress, cancellationToken).ConfigureAwait(false))
    {
      // downloaded
    }
    else if (update.AssetId > 0
             && await TryDownloadReleaseAssetAsync(update.AssetId, packagePath, errors, progress, cancellationToken).ConfigureAwait(false))
    {
      // downloaded
    }
    else
    {
      throw new InvalidOperationException(BuildDownloadErrorMessage(errors));
    }

    if (string.IsNullOrWhiteSpace(update.ExpectedSha256))
    {
      File.Delete(packagePath);
      throw new InvalidOperationException("Release enthält keine SHA256-Prüfsumme. Update abgebrochen.");
    }

    ReportProgress(progress, 78, "Prüfsumme wird verifiziert…");
    var actual = AppSecurityService.ComputeSha256Hex(packagePath);
    if (!string.Equals(actual, update.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
    {
      File.Delete(packagePath);
      throw new InvalidOperationException(
        "Update-Prüfsumme ungültig. Download abgebrochen.\nErwartet: "
        + update.ExpectedSha256 + "\nErhalten: " + actual);
    }

    ReportProgress(progress, 82, "Update wird entpackt…");
    ExtractZipWithProgress(packagePath, extractPath, progress, cancellationToken);

    var stagedAppRoot = FindApplicationRoot(extractPath);
    if (string.IsNullOrWhiteSpace(stagedAppRoot)
        || !File.Exists(Path.Combine(stagedAppRoot, AppInfo.ExeFileName)))
      throw new InvalidOperationException("Update-Paket enthält keine gültige " + AppInfo.ExeFileName + ".");

    ReportProgress(progress, 96, "Paket wird geprüft…");
    if (!AppSecurityService.TryVerifyApplicationPackage(stagedAppRoot, out var signatureMessage, stagedAppRoot))
      throw new InvalidOperationException("Update-Paket ungültig: " + signatureMessage);

    ReportProgress(progress, 100, "Download abgeschlossen.");
    return stagedAppRoot;
  }

  public static void LaunchUpdaterAndShutdown(string stagedAppRoot)
  {
    stagedAppRoot = Path.GetFullPath(stagedAppRoot);
    var appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var updaterExePath = Path.Combine(stagedAppRoot, AppInfo.ExeFileName);
    if (!File.Exists(updaterExePath))
      throw new InvalidOperationException("Update-EXE im Paket nicht gefunden: " + updaterExePath);

    var parentProcessId = Environment.ProcessId;
    var arguments = $"--apply-update \"{stagedAppRoot}\" \"{appRoot}\" {parentProcessId}";
    var startInfo = new ProcessStartInfo
    {
      FileName = updaterExePath,
      Arguments = arguments,
      WorkingDirectory = stagedAppRoot,
      UseShellExecute = true
    };

    if (AppInfo.IsProtectedInstallDirectory(appRoot))
      startInfo.Verb = "runas";

    Process.Start(startInfo);
    Application.Current.Shutdown();
  }

  public static bool TryParseReleaseVersion(string tag, out Version version)
  {
    version = new Version(0, 0);
    if (string.IsNullOrWhiteSpace(tag))
      return false;

    var cleaned = tag.Trim().TrimStart('v', 'V');
    var numeric = Regex.Match(cleaned, @"^(\d+)\.(\d+)(?:\.(\d+))?(\.(\d+))?");
    if (!numeric.Success)
      return false;

    var major = int.Parse(numeric.Groups[1].Value);
    var minor = int.Parse(numeric.Groups[2].Value);
    var build = numeric.Groups[3].Success ? int.Parse(numeric.Groups[3].Value) : 0;
    if (numeric.Groups[5].Success)
      build = int.Parse(numeric.Groups[5].Value);

    var revision = Regex.Match(cleaned, @"R(\d+)", RegexOptions.IgnoreCase);
    if (revision.Success)
      build = int.Parse(revision.Groups[1].Value);

    version = new Version(major, minor, build);
    return true;
  }

  private static async Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
  {
    if (AppInfo.GitHubUpdatesArePublic)
    {
      try
      {
        return await FetchLatestReleaseViaPublicWebAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception webEx)
      {
        try
        {
          return await FetchLatestReleaseViaApiAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception apiEx)
        {
          throw new InvalidOperationException(DescribeGitHubFetchError(webEx, apiEx));
        }
      }
    }

    return await FetchLatestReleaseViaApiAsync(cancellationToken).ConfigureAwait(false);
  }

  private static async Task<string> FetchLatestReleaseViaApiAsync(CancellationToken cancellationToken)
  {
    using var client = CreateHttpClient();
    using var response = await client.GetAsync(AppInfo.GitHubLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
      var detail = await TryReadGitHubErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
      throw new InvalidOperationException(FormatGitHubHttpError("API", response.StatusCode, detail));
    }

    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Öffentliche Releases ohne api.github.com – vermeidet das unauthentifizierte API-Rate-Limit (60/h),
  /// das sonst als 403 Forbidden erscheint.
  /// </summary>
  private static async Task<string> FetchLatestReleaseViaPublicWebAsync(CancellationToken cancellationToken)
  {
    var tag = await ResolveLatestReleaseTagAsync(cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(tag))
      throw new InvalidOperationException("GitHub-Release-Tag konnte nicht ermittelt werden.");

    var notes = await TryLoadReleaseNotesFromAtomAsync(tag, cancellationToken).ConfigureAwait(false)
                ?? string.Empty;
    var assetName = BuildReleaseAssetFileName(tag);
    var downloadUrl =
      $"https://github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/download/{tag}/{assetName}";

    // Minimales JSON im API-Format, damit die bestehende Auswertung weiterläuft.
    return
      "{"
      + "\"tag_name\":\"" + EscapeJson(tag) + "\","
      + "\"body\":\"" + EscapeJson(notes) + "\","
      + "\"assets\":[{"
      + "\"id\":1,"
      + "\"name\":\"" + EscapeJson(assetName) + "\","
      + "\"browser_download_url\":\"" + EscapeJson(downloadUrl) + "\""
      + "}]"
      + "}";
  }

  private static async Task<string?> ResolveLatestReleaseTagAsync(CancellationToken cancellationToken)
  {
    using var client = CreateHttpClient();
    using var request = new HttpRequestMessage(
      HttpMethod.Get,
      $"https://github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest");
    using var response = await client.SendAsync(
      request,
      HttpCompletionOption.ResponseHeadersRead,
      cancellationToken).ConfigureAwait(false);

    var location = response.Headers.Location?.ToString();
    if (string.IsNullOrWhiteSpace(location) && response.RequestMessage?.RequestUri is not null)
      location = response.RequestMessage.RequestUri.ToString();

    if (string.IsNullOrWhiteSpace(location) && response.IsSuccessStatusCode)
    {
      var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      var htmlMatch = Regex.Match(
        html,
        @"/releases/tag/(?<tag>v?[\w.\-]+)",
        RegexOptions.IgnoreCase);
      if (htmlMatch.Success)
        return htmlMatch.Groups["tag"].Value;
    }

    if (string.IsNullOrWhiteSpace(location))
    {
      if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException(
          FormatGitHubHttpError("Web", response.StatusCode, null));
      return null;
    }

    var match = Regex.Match(location, @"/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase);
    return match.Success ? Uri.UnescapeDataString(match.Groups["tag"].Value) : null;
  }

  private static async Task<string?> TryLoadReleaseNotesFromAtomAsync(
    string tag,
    CancellationToken cancellationToken)
  {
    try
    {
      using var client = CreateHttpClient();
      using var response = await client.GetAsync(
        $"https://github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases.atom",
        cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
        return null;

      var atom = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      var entries = Regex.Matches(
        atom,
        @"<entry>(?<body>.*?)</entry>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

      Match? best = null;
      foreach (Match entry in entries)
      {
        var body = entry.Groups["body"].Value;
        if (body.Contains("/releases/tag/" + tag, StringComparison.OrdinalIgnoreCase)
            || body.Contains(">" + tag + "<", StringComparison.OrdinalIgnoreCase)
            || body.Contains(tag, StringComparison.OrdinalIgnoreCase))
        {
          best = entry;
          break;
        }
      }

      best ??= entries.Count > 0 ? entries[0] : null;
      if (best is null)
        return null;

      var contentMatch = Regex.Match(
        best.Groups["body"].Value,
        @"<content[^>]*>(?<html>.*?)</content>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
      if (!contentMatch.Success)
        return null;

      return HtmlToPlainText(WebUtility.HtmlDecode(contentMatch.Groups["html"].Value));
    }
    catch
    {
      return null;
    }
  }

  private static string BuildReleaseAssetFileName(string tag)
  {
    if (TryParseReleaseVersion(tag, out var version) && version.Build > 0)
      return AppInfo.UpdateAssetBaseName + "-R" + version.Build + ".zip";

    var revision = Regex.Match(tag, @"R(\d+)", RegexOptions.IgnoreCase);
    if (revision.Success)
      return AppInfo.UpdateAssetBaseName + "-R" + revision.Groups[1].Value + ".zip";

    return AppInfo.UpdateAssetFileName;
  }

  private static string HtmlToPlainText(string html)
  {
    if (string.IsNullOrWhiteSpace(html))
      return string.Empty;

    var text = html;
    text = Regex.Replace(text, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"</\s*p\s*>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"</\s*li\s*>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<\s*li[^>]*>", "- ", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<[^>]+>", string.Empty);
    text = WebUtility.HtmlDecode(text);
    text = Regex.Replace(text, @"[ \t]+\n", "\n");
    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    return text.Trim();
  }

  private static string EscapeJson(string value)
  {
    if (string.IsNullOrEmpty(value))
      return string.Empty;

    return value
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("\"", "\\\"", StringComparison.Ordinal)
      .Replace("\r", "\\r", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal)
      .Replace("\t", "\\t", StringComparison.Ordinal);
  }

  private static async Task<string?> TryReadGitHubErrorMessageAsync(
    HttpResponseMessage response,
    CancellationToken cancellationToken)
  {
    try
    {
      var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      var message = ExtractJsonString(body, "message");
      return string.IsNullOrWhiteSpace(message) ? null : message;
    }
    catch
    {
      return null;
    }
  }

  private static string FormatGitHubHttpError(string channel, HttpStatusCode statusCode, string? detail)
  {
    if (statusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429
        || (!string.IsNullOrWhiteSpace(detail)
            && detail.Contains("rate limit", StringComparison.OrdinalIgnoreCase)))
    {
      return "GitHub-Update vorübergehend nicht erreichbar (Rate-Limit). "
             + "Bitte in etwa einer Stunde erneut prüfen.";
    }

    var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : ": " + detail;
    return $"GitHub-Release konnte nicht gelesen werden ({channel} {(int)statusCode} {statusCode}){suffix}";
  }

  private static string DescribeGitHubFetchError(Exception webEx, Exception apiEx)
  {
    if (apiEx.Message.Contains("Rate-Limit", StringComparison.OrdinalIgnoreCase)
        || webEx.Message.Contains("Rate-Limit", StringComparison.OrdinalIgnoreCase))
      return "GitHub-Update vorübergehend nicht erreichbar (Rate-Limit). "
             + "Bitte in etwa einer Stunde erneut prüfen.";

    return "GitHub-Release konnte nicht gelesen werden.\n"
           + "Web: " + webEx.Message + "\n"
           + "API: " + apiEx.Message;
  }

  private static async Task<bool> TryDownloadBrowserAssetAsync(
    string downloadUrl,
    string packagePath,
    ICollection<string> errors,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken)
  {
    if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
        || !AppSecurityService.IsTrustedDownloadUrl(uri))
    {
      errors.Add("Unsicherer Download-Link blockiert.");
      return false;
    }

    try
    {
      using var client = CreateHttpClient();
      using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        errors.Add($"Download fehlgeschlagen ({(int)response.StatusCode}).");
        return false;
      }

      await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
      await using var file = File.Create(packagePath);
      await CopyStreamWithProgressAsync(stream, file, response.Content.Headers.ContentLength, 5, 75, progress, cancellationToken).ConfigureAwait(false);
      return true;
    }
    catch (Exception ex)
    {
      errors.Add(ex.Message);
      return false;
    }
  }

  private static async Task<bool> TryDownloadReleaseAssetAsync(
    long assetId,
    string packagePath,
    ICollection<string> errors,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken)
  {
    var apiUrl = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/assets/{assetId}";
    if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
        || !AppSecurityService.IsTrustedDownloadUrl(uri))
    {
      errors.Add("Unsicherer API-Download-Link blockiert.");
      return false;
    }

    try
    {
      using var client = CreateHttpClient();
      using var request = new HttpRequestMessage(HttpMethod.Get, uri);
      request.Headers.Accept.Clear();
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        errors.Add($"API-Download fehlgeschlagen ({(int)response.StatusCode}).");
        return false;
      }

      await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
      await using var file = File.Create(packagePath);
      await CopyStreamWithProgressAsync(stream, file, response.Content.Headers.ContentLength, 5, 75, progress, cancellationToken).ConfigureAwait(false);
      return true;
    }
    catch (Exception ex)
    {
      errors.Add(ex.Message);
      return false;
    }
  }

  private static HttpClient CreateHttpClient()
  {
    var handler = new HttpClientHandler
    {
      AllowAutoRedirect = true,
      MaxAutomaticRedirections = 8
    };
    var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    // GitHub verlangt einen gültigen User-Agent; Leerzeichen/Umlaute in ProductInfoHeaderValue vermeiden.
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
      "RohreZuschnittOptimierung/" + AppInfo.ApplicationVersion.ToString(3));
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/atom+xml");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
    return client;
  }

  private static string? FindApplicationRoot(string extractPath)
  {
    if (File.Exists(Path.Combine(extractPath, AppInfo.ExeFileName)))
      return extractPath;

    var nested = Directory.GetFiles(extractPath, AppInfo.ExeFileName, SearchOption.AllDirectories)
      .OrderBy(path => path.Length)
      .FirstOrDefault();

    return nested is null ? null : Path.GetDirectoryName(nested);
  }

  private sealed class ReleaseAssetInfo
  {
    public long AssetId { get; init; }
    public string? Name { get; init; }
    public string? DownloadUrl { get; init; }
  }

  private static ReleaseAssetInfo? ExtractPreferredAsset(string json)
  {
    var preferred = FindAssetNearName(json, AppInfo.UpdateAssetFileName);
    if (preferred is not null)
      return preferred;

    foreach (Match match in Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
    {
      var name = match.Groups[1].Value;
      if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        continue;

      if (name.Contains("Rohre", StringComparison.OrdinalIgnoreCase)
          || name.Contains("Zuschnitt", StringComparison.OrdinalIgnoreCase))
        return FindAssetNearName(json, name, match.Index);
    }

    return null;
  }

  private static ReleaseAssetInfo? FindAssetNearName(string json, string assetName, int? nameIndex = null)
  {
    int index;
    if (nameIndex.HasValue)
      index = nameIndex.Value;
    else
    {
      var nameMatch = Regex.Match(json, "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) + "\"", RegexOptions.IgnoreCase);
      if (!nameMatch.Success)
        return null;
      index = nameMatch.Index;
    }

    var sliceStart = Math.Max(0, index - 900);
    var sliceLength = Math.Min(json.Length - sliceStart, 1200);
    var slice = json.Substring(sliceStart, sliceLength);

    long assetId = 0;
    foreach (Match idMatch in Regex.Matches(slice, "\"id\"\\s*:\\s*(\\d+)"))
    {
      if (long.TryParse(idMatch.Groups[1].Value, out var parsed))
        assetId = parsed;
    }

    if (assetId <= 0)
      return null;

    return new ReleaseAssetInfo
    {
      AssetId = assetId,
      Name = assetName,
      DownloadUrl = FindDownloadUrlNearIndex(json, index)
    };
  }

  private static string? FindDownloadUrlNearIndex(string json, int nameIndex)
  {
    if (nameIndex < 0 || nameIndex >= json.Length)
      return null;

    var sliceLength = Math.Min(2500, json.Length - nameIndex);
    var slice = json.Substring(nameIndex, sliceLength);
    var urlMatch = Regex.Match(slice, "\"browser_download_url\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase);
    return urlMatch.Success ? UnescapeJson(urlMatch.Groups[1].Value) : null;
  }

  private static string ExtractSha256FromReleaseNotes(string releaseNotes)
  {
    if (string.IsNullOrWhiteSpace(releaseNotes))
      return string.Empty;

    var match = Regex.Match(releaseNotes, @"SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})", RegexOptions.IgnoreCase);
    return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
  }

  private static string? ExtractJsonString(string json, string propertyName)
  {
    var match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
    return match.Success ? UnescapeJson(match.Groups[1].Value) : null;
  }

  private static string UnescapeJson(string value)
  {
    if (string.IsNullOrEmpty(value))
      return string.Empty;

    var builder = new System.Text.StringBuilder(value.Length);
    for (var index = 0; index < value.Length; index++)
    {
      var character = value[index];
      if (character != '\\' || index + 1 >= value.Length)
      {
        builder.Append(character);
        continue;
      }

      switch (value[++index])
      {
        case '"': builder.Append('"'); break;
        case '\\': builder.Append('\\'); break;
        case '/': builder.Append('/'); break;
        case 'b': builder.Append('\b'); break;
        case 'f': builder.Append('\f'); break;
        case 'n': builder.Append('\n'); break;
        case 'r': builder.Append('\r'); break;
        case 't': builder.Append('\t'); break;
        case 'u' when index + 4 < value.Length:
          var hex = value.Substring(index + 1, 4);
          if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var codePoint))
          {
            builder.Append((char)codePoint);
            index += 4;
          }
          else
          {
            builder.Append('\\');
            builder.Append('u');
          }

          break;
        default:
          builder.Append('\\');
          builder.Append(value[index]);
          break;
      }
    }

    return builder.ToString();
  }

  private static string DescribeUpdateError(Exception ex) =>
    ex is TaskCanceledException
      ? "Update-Prüfung hat zu lange gedauert. Bitte Internetverbindung prüfen."
      : ex.Message;

  private static string BuildDownloadErrorMessage(IEnumerable<string> errors)
  {
    var details = string.Join("\n", errors.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct());
    return string.IsNullOrWhiteSpace(details)
      ? "Update-Paket konnte nicht heruntergeladen werden."
      : "Update-Paket konnte nicht heruntergeladen werden.\n\n" + details;
  }

  private static void ReportProgress(IProgress<UpdateProgressInfo>? progress, int percent, string message) =>
    progress?.Report(new UpdateProgressInfo(percent, message));

  private static async Task CopyStreamWithProgressAsync(
    Stream source,
    Stream destination,
    long? totalBytes,
    int percentStart,
    int percentEnd,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken)
  {
    const int bufferSize = 81920;
    var buffer = new byte[bufferSize];
    long totalRead = 0;
    int read;

    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
    {
      await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
      totalRead += read;

      var percent = percentStart;
      if (totalBytes is > 0)
        percent = percentStart + (int)(totalRead * (percentEnd - percentStart) / totalBytes.Value);
      else if (totalRead > 0)
        percent = Math.Min(percentEnd - 1, percentStart + (int)(totalRead / 250000));

      ReportProgress(progress, percent, "Update wird heruntergeladen…");
    }
  }

  private static void ExtractZipWithProgress(
    string zipFilePath,
    string destinationDirectory,
    IProgress<UpdateProgressInfo>? progress,
    CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(destinationDirectory);
    var root = Path.GetFullPath(destinationDirectory);
    if (!root.EndsWith(Path.DirectorySeparatorChar))
      root += Path.DirectorySeparatorChar;

    using var archive = ZipFile.OpenRead(zipFilePath);
    var entries = archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).ToArray();
    var total = Math.Max(1, entries.Length);

    for (var index = 0; index < entries.Length; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var entry = entries[index];
      var entryPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
      if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unsicherer ZIP-Eintrag blockiert: " + entry.FullName);

      var entryDir = Path.GetDirectoryName(entryPath);
      if (!string.IsNullOrWhiteSpace(entryDir))
        Directory.CreateDirectory(entryDir);

      entry.ExtractToFile(entryPath, true);
      ReportProgress(progress, 82 + (int)((index + 1) * 12.0 / total), "Entpacke: " + entry.Name);
    }
  }
}
