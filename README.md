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

## USB-Version (wie bisher)

```powershell
.\create-usb-version.ps1
```

Erzeugt:
- `USB-Version\Rohre-Zuschnitt-Rxx\` (portabel)
- `USB-Version\Rohre-Zuschnitt-Rxx.zip` (für USB-Stick)
- Kopie nach `Z:\Rohre-Zuschnitt-Rxx\` und `Z:\Rohre-Zuschnitt-Rxx.zip`

## Absicherung Quellcode auf Z:

```powershell
.\Sicherung-USB.ps1 -DestinationRoot "Z:\"
```

Legt unter `Z:\Rohre-Zuschnitt-Optimierung\` das Programm und ein Quellcode-ZIP ab (zusätzlich zum GitHub-Stand).
