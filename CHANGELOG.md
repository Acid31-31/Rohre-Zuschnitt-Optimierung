# Changelog – Rohre Zuschnitt Optimierung

Jede Revision listet die sichtbaren Änderungen für Nutzer.
Beim Release liest `publish-github-release.ps1` den Abschnitt zur aktuellen Revision.

## R41

- Freischaltung wie DOK-V01: Lizenzschlüssel im Ablauf-Fenster zur Vollversion
- PC-Code anzeigen; nach Freischalten läuft die App ohne Testfrist weiter

## R40

- Testlaufzeit wieder 90 Tage; nach diesem Update startet die Frist neu
- Abgelaufene Testversion: Update prüfen bleibt möglich (nicht mehr nur „Schließen“)

## R39

- Update-Anzeige listet alle Änderungen seit der installierten Version (nicht nur die neueste)
- Ein PC der mehrere Revisionen übersprungen hat, sieht R37, R38, R39 usw. in einem Update

## R38

- Update-Anzeige zeigt die Paketgröße in MB (wie DOK-V01)
- Beim Download: Fortschritt als „X von Y MB“

## R37

- Zuschnittplan: Rohre als Rechtecke, Farbe folgt der Gehrung (nicht der Länge)
- Gleiche Gehrung am Stoß = eine schwarze Schräge (ein Schnitt für beide Enden)
- 90°-Schnitte als schwarze Senkrechte in der Zeichnung (sonst fehlen sie in der Liste)
- Keine Kästchenränder und keine roten Schnittfugen in der Stangenzeichnung
- PDF quer, Titel mit Profil und Format; Sägetabelle mit Anschlag, Winkel und Hinweis

## R36

- Zeichnungserkennung: C-/U-/T-Profile und Vollstangen werden erkannt
- Excel-Positionen: gleiche Profilarten zuordenbar

## R35

- Auftragsimport: mehrere Rohrprofile werden erkannt und getrennt berechnet
- Teilliste zeigt das Profil je Zeichnung
- Optimieren erzeugt Zuschnittplan je Profil (nicht mehr alles auf ein Maß)

## R34

- Lager: Löschen wird jetzt auf allen PCs übernommen (nicht mehr durch Sync rückgängig gemacht)
- Neue Materialien anderer PCs bleiben beim Speichern erhalten

## R33

- Lager-Speichern auf Netzwerkordner stabilisiert (lokales Temp + Copy statt SMB-Replace)
- Fehler „Nicht genügend Systemressourcen“ / Netzwerk beschäftigt behoben
- Online-Heartbeat und Lager-Abfrage entlasten das Netzlaufwerk
## R32

- Gemeinsames Lager: mehrere PCs können gleichzeitig anlegen/ändern (kein Datei-Sperr-Fehler mehr)
- Änderungen erscheinen automatisch auf allen offenen Lagern

- Start-Hänger auf PCs mit gemeinsamem Lager-Ordner behoben (kein UI-Block mehr beim Aktivieren/Laden)
- Lager-Datei: Timeout beim Öffnen, damit SMB-Hänger die App nicht einfrieren
- Update-Prüfung beim Start mit kurzem Timeout (max. 12 s)
- Lager-Polling ohne DispatcherTimer vom Hintergrund-Thread
## R31

- Start-Hänger behoben: Lager/Netzwerk blockiert die Oberfläche nach Update nicht mehr
- Gemeinsamer Lager-Ordner: keine Endlosschleife bei Online-Anzeige
- Lager-Init und Online-Status laufen im Hintergrund
- FileSystemWatcher auf Netzlaufwerken deaktiviert (Polling statt Hänger)
## R30

- Gemeinsamer Lager-Ordner: alle PCs nutzen denselben Netzwerkordner (auch wenn ein PC aus ist)
- Lager-Datei `pipe-warehouse.xml` wird automatisch im gewählten Ordner angelegt
- Online-Anzeige in der Kopfzeile: wer gerade mit dem Lager verbunden ist
- Neue Lager-Profile: C-/U-/T-Profile und Vollstangen Ø 5–15 mm (Standardlänge 6000 mm)
- Lager-Zentrale ohne Admin-Rechte (TcpListener); SQLite bei Netzlaufwerk lokal abgelegt
## R29

- Update-Hotfix: nur noch standalone-Pakete (kein .NET-Installationsdialog mehr)
- Update-Installation kopiert jetzt alle Dateien vollständig
- Desktop-Verknüpfung und Logo werden nach Update korrekt repariert
- Update-Fenster mit lesbarem Fortschritt und Restlaufzeit

## R28

- Werkstattzeichnung A3 im Tesla-Format mit ISO-Schriftfeld und korrektem Logo
- Mehrere Rohre: alle Zeichnungen in einer PDF (Seite 1, 2, 3 …)
- Schriftfeld-Text neben dem Logo automatisch an die Zelle angepasst
- Kopfzeilen-Buttons (Update, Einstellungen, Info …) mit lesbarer Schriftgröße
- Z-Freigabe: alte Revisionen werden nach `Archiv\` verschoben (nicht gelöscht)
- Nur noch **ein** USB-Ordner (`Z:\Rohre-Zuschnitt\`) und **eine** Absicherung (`Z:\Programierung\Rohre-Zuschnitt-Absicherung\`)

## R27

- App-Icon korrigiert: nur Hexagon-R (ohne Textreste), zentriert, abgerundeter schwarzer Hintergrund

## R26

- App-Icon: Hexagon-R-Logo zentriert, schwarzer Hintergrund mit abgerundeten Ecken (Desktop-Verknüpfung)

## R25

- Update-Fix: Self-Contained-Pakete kopieren jetzt auch `.json` (runtimeconfig/deps) – sonst .NET-Installationsdialog nach Update
- Desktop-/Festinstallation wird durch Releases nicht mehr angefasst

## R24

- Desktop-Verknüpfung und App-Icon nutzen dasselbe Hexagon-R-Logo wie in der App

## R23

- Standalone-Paket: läuft auf anderen PCs ohne separate .NET-Installation
- USB/GitHub-Paket ohne KI-Ordner (Vision-KI separat installieren) – schnelle Updates
- Diagnose-Start.bat prüft EXE und Start

## R22

- Einstellungen aufgeteilt: Optimierung, Netzwerk, PDF-Zuschnittplan, Vision-KI (eigene Fenster)
- Lager-Zentrale für mehrere PCs (SQLite + HTTP, kein gemeinsamer Dateiordner)
- Host-Adresse mit Port zum Kopieren in den Netzwerkeinstellungen
- Lager-Mengenbearbeitung per Doppelklick; Spalte „Menge“
- PDF-Vorschau maximiert mit weißem Papierhintergrund
- Gesamtbearbeitungszeit im Schnittplan und PDF

## R21

- Neues Design wiederhergestellt (Logo-Kopfleiste, Planung / Schnittplan)
- Größeres App-Logo in der Kopfleiste
- Ein Quellordner auf Z: — alte Design-Kopien entfernt
- USB-Version und GitHub-Update für bereits installierte Revisionen

## R20

- Komplette Absicherung auf Laufwerk Z: (Programmordner, ZIP und Quellcode)
- GitHub-Release R20 inkl. Quellcode-Stand
- Standardpfad nach Ausfall von E: dauerhaft Z:

## R19

- Fertige USB-Version: Starten, optional einrichten, Deinstallieren – direkt aus dem Ordner
- Lokale Vision-KI vollständig im Programmordner (Ollama und Modelle)
- Quellcode und Programmstand auf GitHub gesichert

## R18

- Echte lokale Vision-KI ist im Programmordner unter AI\ mitgeliefert (kein separates Ollama-Setup)
- Liest Rohrlänge und Gehrung aus der Zeichnung; Zeichnungen bleiben auf localhost
- Windows-OCR allein entfällt als Hauptlösung

## R17

- Zwischenstand Zeichnungserkennung (OCR) – durch R18 ersetzt

## R16

- Vorbereitung lokale Zeichnungserkennung

## R15

- Desktop-Verknüpfung wird nach Updates und beim Start automatisch repariert, wenn das Ziel fehlt
- Beim Erstellen neuer USB-Versionen werden ältere Programmordner nicht mehr gelöscht

## R14

- App-Icon: 3D-Rohr-Monogramm „R“ (EXE, Taskleiste und Fenster)

## R13

- Update-Prüfung funktioniert wieder, auch wenn GitHub die API-Rate-Limits sperrt (403)
- Releases werden über die öffentliche GitHub-Seite gelesen statt nur über die API

## R12

- Rohre aus der Bestell-Excel ohne Zeichnungsnummer und ohne PDF werden trotzdem in die Teilliste übernommen
- Dafür erzeugt die App eine Werkstattzeichnung (Profil, Länge, Stückzahl, Position in der Baugruppe)
- Die Rohrlänge wird aus der Excel-Beschreibung oder aus der Stückliste der Hauptzeichnung gelesen

## R11

- Stückzahl aus der Bestell-Excel (Requested Amount), Rohrlänge aus der zugehörigen Zeichnung
- Fehlt das Längenmaß im PDF-Text, wird die Länge aus der STEP-Datei derselben Zeichnung gelesen
- Gehrung nur noch bei klarer Angabe – keine Fehlwerte aus Schriftfeld oder Dateipfad

## R10

- Nur echte Rohrzeichnungen (z. B. Tesla „TUBE“) kommen in die Teilliste – Bleche, Cover und Halter nicht
- Profil und Material werden nur aus Rohrzeichnungen ermittelt
- Abweichende Profilmaße werden übersprungen, wenn bereits ein Rohrprofil gewählt ist

## R9

- Tesla-/ISO-Zeichnungen: Rohrlänge auch ohne „mm“-Angabe aus Maßzahlen erkannt (z. B. 1199,6)
- ZIP ohne Excel: Hinweis klarer; Excel (.xlsx) zusätzlich per Drag & Drop oder Dateiauswahl möglich
- Bestellmenge aus .xlsx/.xlsm (wenn vorhanden) weiterhin automatisch

## R8

- Planung in 3 klaren Schritten (Auftrag/Profil, Teile, Optimieren)
- Originalstange in die Einstellungen verschoben (wie Schnittbreite)
- PDF/ZIP per Drag & Drop statt Ordner suchen; erkannte Rohre automatisch in die Teilliste
- Automatische Erkennung von Rohrprofil und Material aus Zeichnungen
- Vor dem Optimieren Projektuebersicht aller Rohre; zu lange Teile werden markiert
- PDF-Laengenerkennung ignoriert Profilmasse (weniger Fehllesungen)

## R7

- Testversion mit 30 Tagen Laufzeit ab erstem Start nach diesem Update
- Willkommenshinweis beim ersten Start der Testversion
- Fenstertitel und Ueber-Dialog zeigen verbleibende Testtage
- Nach Ablauf blockiert die App den Start mit Hinweisfenster

## R6

- Schnittbreite (mm) vom Hauptbildschirm in die Einstellungen verschoben
- Schnittbreite wird dauerhaft gespeichert und gilt fuer alle Optimierungen
- Einstellungsfenster mit Bereich Optimierung und PDF-Ausgabe erweitert

## R5

- TEST: Deutsche Umlaute in Update-Beschreibungen werden korrekt angezeigt
- TEST: Zeilenumbrueche in Release-Notizen werden korrekt gelesen (keine sichtbaren \\r\\n mehr)
- TEST: Jede Aenderung erscheint als eigene Zeile unter „Aenderungen in diesem Update“

## R4

- TEST: Dieses Update dient nur zum Prüfen der Update-Funktion
- TEST: Im Update-Fenster soll jede Änderung als eigene Textzeile sichtbar sein
- TEST: Nach der Installation zeigt „Über die Anwendung“ die Version 1.0 R4
- TEST: Der Hell/Dunkel-Schalter oben rechts bleibt unverändert verfügbar

## R3

- Update-Fenster listet jede Änderung als eigene Textzeile mit Aufzählungspunkt
- Changelog-Datei (`CHANGELOG.md`) steuert die Release-Beschreibung bei jedem Update
- Technische SHA256-Zeile wird im Update-Dialog nicht mehr angezeigt

## R2

- Sichtbarer Hell/Dunkel-Schalter oben rechts in der Menüleiste
- Theme-Umschaltung zusätzlich unter Einstellungen
- Alle Arbeitsdaten portabel im Programmordner unter `Daten\` (nicht mehr in AppData)
- Einmalige Migration alter Daten aus AppData beim ersten Start
- Automatische Updates über GitHub mit Prüfsummen- und Signaturprüfung

## R1

- Erste öffentliche Version mit Lagerverwaltung und Auftragsführung
- Zuschnittplan als PDF exportieren
- USB-Version mit optionalem Einrichtungsassistenten
- Automatische Update-Prüfung über GitHub
