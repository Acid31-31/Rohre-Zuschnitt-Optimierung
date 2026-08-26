# Changelog – Rohre Zuschnitt Optimierung

Jede Revision listet die sichtbaren Änderungen für Nutzer.
Beim Release liest `publish-github-release.ps1` den Abschnitt zur aktuellen Revision.

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
