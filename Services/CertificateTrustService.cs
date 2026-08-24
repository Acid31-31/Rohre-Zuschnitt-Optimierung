using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace RohreZuschnittOptimierung.Services;

internal static class CertificateTrustService
{
  public static bool TryInstallPublisherCertificate()
  {
    var cerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppInfo.CodeSigningCerFileName);
    if (!File.Exists(cerPath))
      return false;

    try
    {
      using var certificate = new X509Certificate2(cerPath);
      if (IsPublisherTrusted(certificate))
        return true;

      AddToStoreIfMissing(StoreName.TrustedPublisher, certificate);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool IsPublisherTrusted(X509Certificate2 certificate)
  {
    try
    {
      using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
      store.Open(OpenFlags.ReadOnly);
      return store.Certificates.Cast<X509Certificate2>()
        .Any(existing => string.Equals(existing.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));
    }
    catch
    {
      return false;
    }
  }

  private static void AddToStoreIfMissing(StoreName storeName, X509Certificate2 certificate)
  {
    using var store = new X509Store(storeName, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadWrite);
    if (!store.Certificates.Cast<X509Certificate2>()
          .Any(existing => string.Equals(existing.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
    {
      store.Add(certificate);
    }
  }
}
