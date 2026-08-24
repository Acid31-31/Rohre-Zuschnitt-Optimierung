namespace RohreZuschnittOptimierung.Models;

public sealed class TrialLicenseStatus
{
  public bool IsTrialEdition { get; set; }

  public bool IsExpired { get; set; }

  public bool IsFirstRun { get; set; }

  public DateTime FirstRunLocal { get; set; }

  public DateTime ExpiresLocal { get; set; }

  public int DaysRemaining { get; set; }

  public string TitleSuffix { get; set; } = string.Empty;

  public string SummaryText { get; set; } = string.Empty;

  public string VersionLine { get; set; } = string.Empty;
}
