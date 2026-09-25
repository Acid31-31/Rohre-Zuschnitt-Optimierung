using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RohreZuschnittOptimierung.Services;

/// <summary>
/// Offline-Freischaltung der Testversion zur Vollversion (Lizenzschlüssel), wie DOK-V01.
/// </summary>
internal static class LicenseActivationService
{
  private const string IntegritySalt = "Rohre-Zuschnitt-License-Unlock-v1";
  private const string KeyPrefix = "RZO";

  private static string StorePath => Path.Combine(AppInfo.UserDataDirectory, "license.xml");

  public static bool IsActivated()
  {
    if (!AppInfo.IsTrialEdition)
      return true;

    try
    {
      if (!File.Exists(StorePath))
        return false;

      var doc = XDocument.Load(StorePath);
      var root = doc.Root;
      if (root is null)
        return false;

      var key = ((string?)root.Element("LicenseKey") ?? string.Empty).Trim();
      var machine = ((string?)root.Element("MachineCode") ?? string.Empty).Trim();
      var integrity = ((string?)root.Element("Integrity") ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(integrity))
        return false;

      var currentMachine = GetMachineCode();
      if (string.IsNullOrWhiteSpace(machine)
          || !string.Equals(machine, currentMachine, StringComparison.OrdinalIgnoreCase))
        return false;

      if (!string.Equals(integrity, ComputeStoreIntegrity(key, machine), StringComparison.Ordinal))
        return false;

      return IsValidMachineKey(NormalizeKey(key), currentMachine);
    }
    catch
    {
      return false;
    }
  }

  public static string GetMachineCode()
  {
    var raw = MachineFingerprintService.GetMachineFingerprint();
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    var hex = BytesToHex(hash, 6);
    return FormatGroups(hex, 4);
  }

  public static bool TryActivate(string licenseKey, out string message)
  {
    message = string.Empty;
    if (!AppInfo.IsTrialEdition || IsActivated())
    {
      message = "Diese Installation ist bereits eine Vollversion.";
      return true;
    }

    var normalized = NormalizeKey(licenseKey);
    if (string.IsNullOrWhiteSpace(normalized))
    {
      message = "Bitte einen Lizenzschlüssel eingeben.";
      return false;
    }

    var machineCode = GetMachineCode();
    if (!IsValidMachineKey(normalized, machineCode))
    {
      message = "Ungültiger Schlüssel. Die Einzel-Lizenz gilt nur für diesen PC-Code.";
      return false;
    }

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(StorePath) ?? AppInfo.UserDataDirectory);
      new XDocument(
          new XElement("License",
            new XElement("LicenseType", "SinglePc"),
            new XElement("LicenseKey", normalized),
            new XElement("MachineCode", machineCode),
            new XElement("ActivatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            new XElement("Integrity", ComputeStoreIntegrity(normalized, machineCode))))
        .Save(StorePath);

      message = "Vollversion freigeschaltet (Einzel-Lizenz, nur dieser PC).";
      return true;
    }
    catch (Exception ex)
    {
      message = "Freischaltung fehlgeschlagen: " + ex.Message;
      return false;
    }
  }

  /// <summary>PC-Codes aus Text (eine Zeile je Code, oder Komma/Leerzeichen).</summary>
  public static IReadOnlyList<string> SplitMachineCodes(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return Array.Empty<string>();

    var list = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var part in Regex.Split(text.Trim(), @"[\s,;]+"))
    {
      var code = NormalizeKey(part);
      if (string.IsNullOrWhiteSpace(code) || code.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        continue;
      if (seen.Add(code))
        list.Add(code);
    }

    return list;
  }

  /// <summary>Maschinengebundenen Einzel-Lizenzschlüssel erzeugen (für Anbieter).</summary>
  public static string GenerateMachineKey(string? machineCode = null)
    => GenerateMachineKey(machineCode, "SINGLE");

  private static string GenerateMachineKey(string? machineCode, string kind)
  {
    var code = NormalizeKey(string.IsNullOrWhiteSpace(machineCode) ? GetMachineCode() : machineCode);
    var digest = ComputeKeyDigest(kind + "|" + code);
    return KeyPrefix + "-" + FormatGroups(digest, 4);
  }

  private static bool IsValidMachineKey(string normalizedKey, string machineCode)
  {
    if (string.IsNullOrWhiteSpace(machineCode))
      return false;

    foreach (var kind in new[] { "SINGLE", "FULL" })
    {
      if (string.Equals(
            normalizedKey,
            NormalizeKey(GenerateMachineKey(machineCode, kind)),
            StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  private static string ComputeKeyDigest(string payload)
  {
    using var hmac = new HMACSHA256(GetSecretKey());
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return BytesToHex(hash, 8);
  }

  private static string ComputeStoreIntegrity(string key, string machineCode)
  {
    var payload = (key ?? string.Empty) + "|" + (machineCode ?? string.Empty) + "|" + GetMachineCode();
    using var hmac = new HMACSHA256(GetSecretKey());
    return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
  }

  private static byte[] GetSecretKey()
  {
    using var sha = SHA256.Create();
    return sha.ComputeHash(Encoding.UTF8.GetBytes(AppInfo.ProductName + "|" + IntegritySalt));
  }

  private static string NormalizeKey(string? key)
  {
    if (string.IsNullOrWhiteSpace(key))
      return string.Empty;

    var cleaned = Regex.Replace(key.Trim().ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);
    if (cleaned.StartsWith("RZO", StringComparison.Ordinal))
      return KeyPrefix + "-" + FormatGroups(cleaned[3..], 4);

    return FormatGroups(cleaned, 4);
  }

  private static string FormatGroups(string hexOrAlnum, int groupSize)
  {
    if (string.IsNullOrEmpty(hexOrAlnum))
      return string.Empty;

    var builder = new StringBuilder();
    for (var i = 0; i < hexOrAlnum.Length; i++)
    {
      if (i > 0 && i % groupSize == 0)
        builder.Append('-');
      builder.Append(hexOrAlnum[i]);
    }

    return builder.ToString();
  }

  private static string BytesToHex(byte[] bytes, int takeBytes)
  {
    var count = Math.Min(bytes.Length, Math.Max(1, takeBytes));
    var builder = new StringBuilder(count * 2);
    for (var i = 0; i < count; i++)
      builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
    return builder.ToString();
  }
}
