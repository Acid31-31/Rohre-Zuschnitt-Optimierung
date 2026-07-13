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
      result.ReleaseNotes = ExtractJsonString(json, "body") ?? string.Empty;
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

    version = new Version(major, minor, build);
    return true;
  }

  private static async Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
  {
    using var client = CreateHttpClient();
    using var response = await client.GetAsync(AppInfo.GitHubLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException($"GitHub-Release konnte nicht gelesen werden ({(int)response.StatusCode} {response.StatusCode}).");

    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.DisplayVersion);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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

  private static string UnescapeJson(string value) =>
    (value ?? string.Empty).Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");

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
