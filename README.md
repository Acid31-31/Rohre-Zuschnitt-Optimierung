# Rohre Zuschnitt Optimierung

Desktop-WPF-Anwendung zur Optimierung von Rohrzuschnitten (Stangenlängen, Teileliste, Verschnitt minimieren, Lagerverwaltung, Auftragsführung).

**GitHub:** https://github.com/Acid31-31/Rohre-Zuschnitt-Optimierung

**Standardwerte:** Stangenlänge 6000 mm (6 m), Schnittbreite 3 mm.

## Technik

- .NET 8, WPF
- Eigenes Git-Repository (getrennt von DOK-V01)
- Automatische Updates über GitHub Releases

## Entwicklung (einziger Quellordner)

```powershell
cd "Z:\Programierung\Rohre-Zuschnitt-Optimierung"
dotnet build
dotnet run
```

Alle Entwicklung, USB-Builds und Releases erfolgen nur aus diesem Ordner.

## Release & Updates (für installierte ältere Revisionen)

```powershell
.\publish-github-release.ps1
```

Erstellt `RohreZuschnittOptimierung-Release-Rxx.zip` und lädt es als GitHub-Release hoch. Bereits installierte ältere Versionen prüfen beim Start automatisch auf Updates.

Ab R23 enthält das Update-Paket **keine** Vision-KI mehr (`AI\` separat installieren). Optional mit KI: `.\publish-github-release.ps1 -IncludeAi`.

## USB-Version

```powershell
.\create-usb-version.ps1
```

Erzeugt diese feste Struktur:

**Z:\Rohre-Zuschnitt\** (Hauptordner)
- `Rohre-Zuschnitt-Rxx\` — lauffähiges Programm
- `Rohre-Zuschnitt-Rxx.zip`

**Projekt:** `USB-Version\Rohre-Zuschnitt-Rxx\` + ZIP (gleicher Inhalt, anderer Ordnername)

**Absicherung:** `Z:\Programierung\Rohre-Zuschnitt-Absicherung\`

## Absicherung (nur bei Bedarf erneut)

```powershell
.\Sicherung-USB.ps1
```

Aktualisiert `Z:\Programierung\Rohre-Zuschnitt-Absicherung\` (Programm + Quellcode-ZIP).
