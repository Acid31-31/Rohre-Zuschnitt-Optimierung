using System.IO;
using System.Xml.Linq;
using RohreZuschnittOptimierung.Models;

namespace RohreZuschnittOptimierung.Services;

internal static class UninstallSessionStore
{
  private static readonly string StorePath = Path.Combine(AppInfo.UserDataDirectory, "uninstall-session.xml");

  public static void SavePending(UninstallSessionOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);

    Directory.CreateDirectory(AppInfo.UserDataDirectory);
    new XDocument(
        new XElement("UninstallSession",
          new XElement("ConfirmationAccepted", options.ConfirmationAccepted),
          new XElement("RemoveUserData", options.RemoveUserData),
          new XElement("CreatedUtc", DateTime.UtcNow.ToString("o"))))
      .Save(StorePath);
  }

  public static bool TryConsumePending(out UninstallSessionOptions? options)
  {
    options = null;
    if (!File.Exists(StorePath))
      return false;

    try
    {
      var doc = XDocument.Load(StorePath);
      var root = doc.Root;
      if (root is null)
        return false;

      var createdText = (string?)root.Element("CreatedUtc");
      if (!string.IsNullOrWhiteSpace(createdText)
          && DateTime.TryParse(createdText, out var createdUtc)
          && createdUtc < DateTime.UtcNow.AddHours(-1))
      {
        Clear();
        return false;
      }

      if (!bool.TryParse((string?)root.Element("ConfirmationAccepted"), out var accepted) || !accepted)
        return false;

      options = new UninstallSessionOptions
      {
        ConfirmationAccepted = true,
        RemoveUserData = ParseBool(root.Element("RemoveUserData"), false)
      };

      Clear();
      return true;
    }
    catch
    {
      Clear();
      return false;
    }
  }

  public static void Clear()
  {
    try
    {
      if (File.Exists(StorePath))
        File.Delete(StorePath);
    }
    catch
    {
    }
  }

  private static bool ParseBool(XElement? element, bool defaultValue) =>
    element is null
      ? defaultValue
      : bool.TryParse((string?)element, out var value) ? value : defaultValue;
}
