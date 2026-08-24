using System.Security.Cryptography;
using System.Text;

namespace RohreZuschnittOptimierung.Services;

internal static class MachineFingerprintService
{
  public static string GetMachineFingerprint()
  {
    var material = string.Join("|",
      Environment.MachineName,
      Environment.UserName,
      Environment.OSVersion.VersionString,
      Environment.ProcessorCount);

    using var sha = SHA256.Create();
    return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(material)));
  }
}
