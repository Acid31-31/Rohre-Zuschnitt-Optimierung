using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RohreZuschnittOptimierung.Services;

internal static class AppSecurityService
{
  private static bool _initialized;

  public static void Initialize()
  {
    if (_initialized)
      return;

    _initialized = true;
    EnsureSecureTransport();
  }

  public static void EnsureSecureTransport() =>
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

  public static bool IsTrustedDownloadUrl(Uri uri)
  {
    if (!uri.IsAbsoluteUri)
      return false;

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
      return false;

    var host = uri.Host ?? string.Empty;
    return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
           || host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
           || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
           || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
  }

  public static string SanitizeFileName(string fileName)
  {
    var name = Path.GetFileName(fileName ?? string.Empty);
    if (string.IsNullOrWhiteSpace(name))
      return "datei";

    foreach (var invalid in Path.GetInvalidFileNameChars())
      name = name.Replace(invalid, '_');

    return name.Trim();
  }

  public static string ResolveSafeTargetPath(string targetFolder, string fileName)
  {
    var safeName = SanitizeFileName(fileName);
    var targetPath = Path.GetFullPath(Path.Combine(targetFolder, safeName));
    if (!IsPathInsideDirectory(targetPath, targetFolder))
      throw new InvalidOperationException("Ungueltiger Zielpfad: " + safeName);

    return targetPath;
  }

  public static bool IsPathInsideDirectory(string filePath, string directoryPath)
  {
    var fullFile = Path.GetFullPath(filePath);
    var fullDir = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var prefix = fullDir + Path.DirectorySeparatorChar;
    return fullFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fullFile, fullDir, StringComparison.OrdinalIgnoreCase);
  }

  public static void ExtractZipSafely(string zipFilePath, string destinationDirectory)
  {
    if (!File.Exists(zipFilePath))
      throw new FileNotFoundException("ZIP-Datei nicht gefunden.", zipFilePath);

    Directory.CreateDirectory(destinationDirectory);
    var root = Path.GetFullPath(destinationDirectory);
    if (!root.EndsWith(Path.DirectorySeparatorChar))
      root += Path.DirectorySeparatorChar;

    using var archive = ZipFile.OpenRead(zipFilePath);
    foreach (var entry in archive.Entries)
    {
      if (string.IsNullOrWhiteSpace(entry.Name))
        continue;

      var entryPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
      if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unsicherer ZIP-Eintrag blockiert: " + entry.FullName);

      var entryDir = Path.GetDirectoryName(entryPath);
      if (!string.IsNullOrWhiteSpace(entryDir))
        Directory.CreateDirectory(entryDir);

      entry.ExtractToFile(entryPath, true);
    }
  }

  public static bool TryVerifyApplicationPackage(string packageRoot, out string message, string? trustCerDirectory = null)
  {
    message = string.Empty;
    if (!Directory.Exists(packageRoot))
    {
      message = "Update-Paket nicht gefunden.";
      return false;
    }

    var mainExe = Path.Combine(packageRoot, AppInfo.ExeFileName);
    if (!File.Exists(mainExe))
    {
      message = AppInfo.ExeFileName + " fehlt im Update-Paket.";
      return false;
    }

    if (!AppInfo.RequireCodeSignature)
      return true;

    return TryVerifyFileSignature(mainExe, out message, trustCerDirectory);
  }

  public static bool TryVerifyFileSignature(string filePath, out string message, string? trustCerDirectory = null)
  {
    message = string.Empty;
    try
    {
      if (!File.Exists(filePath))
      {
        message = "Datei nicht gefunden.";
        return false;
      }

      var cert = X509Certificate.CreateFromSignedFile(filePath);
      if (cert is null)
      {
        message = "Keine Authenticode-Signatur.";
        return false;
      }

      using var signedCert = new X509Certificate2(cert);
      if (TryGetTrustedPublisherThumbprint(trustCerDirectory, out var trustedThumbprint)
          && string.Equals(signedCert.Thumbprint, trustedThumbprint, StringComparison.OrdinalIgnoreCase))
        return true;

      if (HasValidCertificateChain(signedCert, out message))
        return true;

      if (!string.IsNullOrWhiteSpace(trustedThumbprint))
        message = "Signatur stammt nicht vom autorisierten Herausgeber.";

      return false;
    }
    catch (Exception ex)
    {
      message = ex.Message;
      return false;
    }
  }

  public static string CreateInstallBackup(string targetRoot)
  {
    if (!Directory.Exists(targetRoot))
      return string.Empty;

    var backupRoot = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");

    if (Directory.Exists(backupRoot))
      Directory.Delete(backupRoot, true);

    CopyApplicationTree(targetRoot, backupRoot);
    return backupRoot;
  }

  public static void RestoreInstallBackup(string backupRoot, string targetRoot)
  {
    if (!Directory.Exists(backupRoot))
      return;

    CopyApplicationTree(backupRoot, targetRoot);
  }

  public static void DeleteDirectorySafe(string? directoryPath)
  {
    try
    {
      if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
        Directory.Delete(directoryPath, true);
    }
    catch
    {
    }
  }

  public static string ComputeSha256Hex(string filePath)
  {
    using var stream = File.OpenRead(filePath);
    using var sha = SHA256.Create();
    return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
  }

  private static bool TryGetTrustedPublisherThumbprint(string? trustCerDirectory, out string thumbprint)
  {
    thumbprint = string.Empty;
    foreach (var directory in new[] { trustCerDirectory, AppDomain.CurrentDomain.BaseDirectory })
    {
      if (string.IsNullOrWhiteSpace(directory))
        continue;

      var cerPath = Path.Combine(directory, AppInfo.CodeSigningCerFileName);
      if (!File.Exists(cerPath))
        continue;

      try
      {
        using var trusted = new X509Certificate2(cerPath);
        thumbprint = trusted.Thumbprint;
        if (!string.IsNullOrWhiteSpace(thumbprint))
          return true;
      }
      catch
      {
      }
    }

    return false;
  }

  private static bool HasValidCertificateChain(X509Certificate2 certificate, out string message)
  {
    message = string.Empty;
    try
    {
      using var chain = new X509Chain();
      chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
      if (!chain.Build(certificate))
      {
        message = "Zertifikatskette ungültig.";
        return false;
      }

      return true;
    }
    catch (Exception ex)
    {
      message = ex.Message;
      return false;
    }
  }

  private static void CopyApplicationTree(string sourceRoot, string targetRoot)
  {
    Directory.CreateDirectory(targetRoot);
    foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
    {
      var relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (!IsApplicationFileExtension(Path.GetExtension(file)))
        continue;

      var destination = Path.Combine(targetRoot, relative);
      var destinationDir = Path.GetDirectoryName(destination);
      if (!string.IsNullOrWhiteSpace(destinationDir))
        Directory.CreateDirectory(destinationDir);

      File.Copy(file, destination, true);
    }
  }

  private static bool IsApplicationFileExtension(string extension) =>
    extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
    || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
}
