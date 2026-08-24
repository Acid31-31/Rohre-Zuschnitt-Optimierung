using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class TrialLicenseService
{
  private const string IntegritySalt = "Rohre-Zuschnitt-Trial-Integrity-v1";

  private static string StorePath => Path.Combine(AppInfo.UserDataDirectory, "trial.xml");

  public static TrialLicenseStatus Evaluate()
  {
    if (!AppInfo.IsTrialEdition)
    {
      return new TrialLicenseStatus
      {
        IsTrialEdition = false,
        IsExpired = false,
        SummaryText = AppInfo.EditionLabel,
        VersionLine = AppInfo.EditionLabel
      };
    }

    var record = LoadOrCreate(DateTime.UtcNow);
    if (record is null || record.Tampered)
      return BuildTamperedStatus();

    var firstRunLocal = record.FirstRunUtc.ToLocalTime().Date;
    var expiresLocal = firstRunLocal.AddDays(AppInfo.TrialPeriodDays);
    var daysRemaining = (expiresLocal - DateTime.Now.Date).Days;
    if (daysRemaining < 0)
      daysRemaining = 0;

    return BuildStatus(record, daysRemaining <= 0, daysRemaining);
  }

  public static void MarkWelcomeShown()
  {
    if (!AppInfo.IsTrialEdition)
      return;

    try
    {
      var record = LoadExisting();
      if (record is null || record.Tampered)
        return;

      record.WelcomeShown = true;
      Save(record);
    }
    catch
    {
      // ignore
    }
  }

  public static bool ShouldShowWelcome()
  {
    if (!AppInfo.IsTrialEdition)
      return false;

    var record = LoadExisting();
    return record is not null && !record.Tampered && !record.WelcomeShown;
  }

  private static TrialRecord LoadOrCreate(DateTime nowUtc)
  {
    var existing = LoadExisting();
    if (existing is not null)
    {
      if (existing.Tampered)
        return existing;

      if (IsClockRolledBack(existing, nowUtc))
      {
        existing.Tampered = true;
        return existing;
      }

      existing.LastRunUtc = nowUtc;
      existing.IsFirstRun = false;
      Save(existing);
      return existing;
    }

    var created = new TrialRecord
    {
      FirstRunUtc = nowUtc,
      LastRunUtc = nowUtc,
      IsFirstRun = true,
      WelcomeShown = false,
      MachineFingerprint = GetMachineFingerprint()
    };
    Save(created);
    return created;
  }

  private static TrialRecord? LoadExisting()
  {
    try
    {
      if (!File.Exists(StorePath))
        return null;

      var doc = XDocument.Load(StorePath);
      var root = doc.Root;
      if (root is null)
        return null;

      var firstRun = ParseUtc((string?)root.Element("FirstRunUtc"));
      if (!firstRun.HasValue)
        return null;

      var lastRun = ParseUtc((string?)root.Element("LastRunUtc")) ?? firstRun.Value;
      var welcomeShown = string.Equals((string?)root.Element("WelcomeShown"), "true", StringComparison.OrdinalIgnoreCase);
      var machineFingerprint = (string?)root.Element("MachineFingerprint") ?? string.Empty;
      var integrity = (string?)root.Element("Integrity") ?? string.Empty;

      var record = new TrialRecord
      {
        FirstRunUtc = firstRun.Value,
        LastRunUtc = lastRun,
        WelcomeShown = welcomeShown,
        MachineFingerprint = machineFingerprint,
        IsFirstRun = false
      };

      if (IsClockTampered(record))
      {
        record.Tampered = true;
        return record;
      }

      if (string.IsNullOrWhiteSpace(integrity))
      {
        record.MachineFingerprint = GetMachineFingerprint();
        Save(record);
        return record;
      }

      if (!string.Equals(machineFingerprint, GetMachineFingerprint(), StringComparison.Ordinal)
          || !string.Equals(integrity, ComputeIntegrity(record), StringComparison.Ordinal))
      {
        record.Tampered = true;
        return record;
      }

      return record;
    }
    catch
    {
      return new TrialRecord { Tampered = true };
    }
  }

  private static bool IsClockRolledBack(TrialRecord record, DateTime nowUtc)
  {
    var tolerance = TimeSpan.FromHours(2);
    return nowUtc.Add(tolerance) < record.LastRunUtc
           || nowUtc.Add(tolerance) < record.FirstRunUtc;
  }

  private static bool IsClockTampered(TrialRecord record)
  {
    var now = DateTime.UtcNow;
    var tolerance = TimeSpan.FromHours(2);
    return record.FirstRunUtc > now.Add(tolerance)
           || record.LastRunUtc > now.Add(tolerance);
  }

  private static TrialLicenseStatus BuildTamperedStatus() =>
    new()
    {
      IsTrialEdition = true,
      IsExpired = true,
      TitleSuffix = " · Testversion ungültig",
      SummaryText = "Die Testversion ist beschädigt oder wurde manipuliert. Bitte die App neu installieren.",
      VersionLine = AppInfo.EditionLabel + " · ungültig"
    };

  private static TrialLicenseStatus BuildStatus(TrialRecord record, bool expired, int daysRemaining)
  {
    var firstRunLocal = record.FirstRunUtc.ToLocalTime().Date;
    var expiresLocal = firstRunLocal.AddDays(AppInfo.TrialPeriodDays);
    var expiresText = expiresLocal.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));

    var titleSuffix = expired
      ? " · Testversion abgelaufen"
      : " · Testversion (" + daysRemaining + " Tag" + (daysRemaining == 1 ? string.Empty : "e") + ")";

    var summary = expired
      ? "Die Testversion ist am " + expiresText + " abgelaufen."
      : "Testversion – noch " + daysRemaining + " Tag" + (daysRemaining == 1 ? string.Empty : "e")
        + " gültig (bis " + expiresText + ").";

    return new TrialLicenseStatus
    {
      IsTrialEdition = true,
      IsExpired = expired,
      IsFirstRun = record.IsFirstRun,
      FirstRunLocal = firstRunLocal,
      ExpiresLocal = expiresLocal,
      DaysRemaining = daysRemaining,
      TitleSuffix = titleSuffix,
      SummaryText = summary,
      VersionLine = AppInfo.EditionLabel + " · gültig bis " + expiresText
    };
  }

  private static void Save(TrialRecord record)
  {
    try
    {
      record.MachineFingerprint = GetMachineFingerprint();
      Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
      new XDocument(
          new XElement("Trial",
            new XElement("FirstRunUtc", record.FirstRunUtc.ToString("o", CultureInfo.InvariantCulture)),
            new XElement("LastRunUtc", record.LastRunUtc.ToString("o", CultureInfo.InvariantCulture)),
            new XElement("WelcomeShown", record.WelcomeShown ? "true" : "false"),
            new XElement("MachineFingerprint", record.MachineFingerprint),
            new XElement("Integrity", ComputeIntegrity(record))))
        .Save(StorePath);
    }
    catch
    {
      // ignore
    }
  }

  private static string ComputeIntegrity(TrialRecord record)
  {
    var payload = string.Join("|",
      record.FirstRunUtc.ToString("o", CultureInfo.InvariantCulture),
      record.LastRunUtc.ToString("o", CultureInfo.InvariantCulture),
      record.WelcomeShown ? "1" : "0",
      record.MachineFingerprint ?? string.Empty);

    using var hmac = new HMACSHA256(GetIntegrityKey());
    return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
  }

  private static byte[] GetIntegrityKey()
  {
    using var sha = SHA256.Create();
    var material = AppInfo.ProductName + "|" + IntegritySalt + "|" + GetMachineFingerprint();
    return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
  }

  private static string GetMachineFingerprint()
  {
    var raw = Environment.MachineName + "|" + Environment.UserDomainName + "|" + Environment.UserName;
    using var sha = SHA256.Create();
    return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
  }

  private static DateTime? ParseUtc(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
      return null;

    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
      return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();

    return null;
  }

  private sealed class TrialRecord
  {
    public DateTime FirstRunUtc { get; set; }

    public DateTime LastRunUtc { get; set; }

    public bool WelcomeShown { get; set; }

    public bool IsFirstRun { get; set; }

    public string MachineFingerprint { get; set; } = string.Empty;

    public bool Tampered { get; set; }
  }
}
