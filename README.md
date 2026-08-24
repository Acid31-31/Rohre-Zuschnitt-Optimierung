# Rohre Zuschnitt Optimierung

Desktop-WPF-Anwendung zur Optimierung von Rohrzuschnitten (Stangenlängen, Teileliste, Verschnitt minimieren, Lagerverwaltung, Auftragsführung).

**GitHub:** https://github.com/Acid31-31/Rohre-Zuschnitt-Optimierung

**Standardwerte:** Stangenlänge 6000 mm (6 m), Schnittbreite 3 mm.

## Technik

- .NET 8, WPF
- Eigenes Git-Repository (getrennt von DOK-V01)
- Automatische Updates über GitHub Releases

## Entwicklung

```powershell
cd "E:\Programierung\Rohre-Zuschnitt-Optimierung"
dotnet build
dotnet run
```

Ausgabe: `bin\Debug\net8.0-windows\RohreZuschnittOptimierung.exe`

## Release & Updates

```powershell
.\publish-github-release.ps1
```

Erstellt `RohreZuschnittOptimierung-Release.zip` mit SHA256 in den Release-Notizen. Die App prüft beim Start automatisch auf Updates.

## USB-Version

```powershell
.\create-usb-version.ps1
```

Erzeugt den portablen Ordner unter `USB-Version\Rohre-Zuschnitt-Rxx`.

## Absicherung (USB-Stick + GitHub)

```powershell
.\Sicherung-USB.ps1
```

Kopiert das lauffähige Programm und ein Quellcode-ZIP auf den USB-Stick (Wechsellaufwerk, z. B. `F:`). Den aktuellen Stand zusätzlich auf GitHub sichern (`git push`).
