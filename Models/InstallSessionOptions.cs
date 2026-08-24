namespace RohreZuschnittOptimierung.Models;

internal sealed class InstallSessionOptions
{
  public bool LicenseAccepted { get; set; }

  public bool InstallPublisherCertificate { get; set; } = true;

  public bool LaunchAfterInstall { get; set; } = true;

  public string SourceDirectory { get; set; } = string.Empty;
}
