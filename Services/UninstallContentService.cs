namespace RohreZuschnittOptimierung.Services;

internal static class UninstallContentService
{
  public static string BuildInformationText()
  {
    return
      "DEINSTALLATION – ROHRE ZUSCHNITT OPTIMIERUNG" + Environment.NewLine
      + new string('=', 58) + Environment.NewLine + Environment.NewLine
      + "Version: " + AppInfo.DisplayVersion + Environment.NewLine
      + "Programmordner: " + AppInfo.GetApplicationDirectory() + Environment.NewLine
      + "Benutzerdaten: " + AppInfo.UserDataDirectory + Environment.NewLine
      + "Desktop-Verknuepfung: " + AppInfo.ShortcutFileName + Environment.NewLine + Environment.NewLine
      + "STANDARDMAESSIG WERDEN ENTFERNT:" + Environment.NewLine
      + "- Desktop-Verknuepfung und Einrichtungsmarkierung" + Environment.NewLine
      + "- Optional alte Installation unter Program Files (falls vorhanden)" + Environment.NewLine + Environment.NewLine
      + "OPTIONAL (Checkbox auf der naechsten Seite):" + Environment.NewLine
      + "- Lagerbestaende, Auftraege, PDF-Einstellungen und weitere Daten in AppData" + Environment.NewLine + Environment.NewLine
      + "HINWEIS:" + Environment.NewLine
      + "Die Deinstallation entfernt Verknuepfung und Einrichtung. Der Programmordner auf USB/Desktop bleibt erhalten." + Environment.NewLine
      + "Nach der Deinstallation kann das Programm erneut ueber den USB-Installer installiert werden." + Environment.NewLine + Environment.NewLine
      + LicenseContentService.GetCopyrightSummary() + Environment.NewLine + Environment.NewLine
      + "LIZENZ (Kurzfassung):" + Environment.NewLine
      + LicenseContentService.LoadLicenseText();
  }
}
