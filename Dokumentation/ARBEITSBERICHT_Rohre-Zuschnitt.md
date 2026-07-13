# Arbeitsbericht & Stundenlohn-Berechnung



## Rohre Zuschnitt Optimierung (Desktop-WPF)



| Feld | Wert |

|------|------|

| **Projekt** | Rohre Zuschnitt Optimierung |

| **Pfad** | `E:\Programierung\Rohre-Zuschnitt-Optimierung` |

| **Technik** | .NET 8, WPF, eigenes Git-Repository |

| **Berichtsdatum** | 12.07.2026 |

| **Leistungszeitraum** | 10.07.2026 – 12.07.2026 |

| **Erstellt von** | Alexander Hölzer |



---



## 1. Kurzbeschreibung



Eigenständige Desktop-Anwendung zur **Optimierung von Rohrzuschnitten** für die Blech-/Rohrbearbeitung. Die App berechnet aus Teilliste, Gehrungen und **Lagerverwaltung** (Profil + Materialart) einen **Schnittplan mit minimalem Verschnitt** und **wenigen Sägeverstellungen**. Nach der Optimierung öffnet sich automatisch eine **druckbare PDF**; bei Materialmangel eine **Bestellliste-PDF**.



---



## 2. Erbrachte Leistungen (Funktionsumfang)



### Phase 1 — Kern (10.–11.07.2026)



| Nr. | Leistung | Status |

|-----|----------|--------|

| 1 | WPF-Projekt (.NET 8), getrennt von DOK-V01 | ✓ |

| 2 | Stangenoptimierung (Best-Fit), Standard 6000 mm, Kerf 3 mm | ✓ |

| 3 | PDF-Zeichnungen einlesen (PdfPig): Länge & Gehrung vorschlagen | ✓ |

| 4 | Manuelle Eingabe, Teilliste, Bearbeiten, Alles löschen | ✓ |

| 5 | Gehrung Ende A / Ende B (0° und 90° = lotrecht) | ✓ |

| 6 | **Gemeinsame Schnitte** (z. B. 90° – 45° gemeinsam – 90°) | ✓ |

| 7 | Ungleiche Gehrungen (z. B. 32°/45° Trapez), korrekte Sägefolge | ✓ |

| 8 | Packung **zuerst nach Gehrung**, dann Verschnitt minimieren | ✓ |

| 9 | Rohreste in Liste — werden vor Neuware verbraucht | ✓ |

| 10 | Grafischer Schnittplan (Trapeze, gemeinsame Schnittlinie) | ✓ |

| 11 | PDF-Export, automatische PDF nach Optimieren | ✓ |

| 12 | Hell-/Dunkelmodus, maximiertes Fenster | ✓ |



### Phase 2 — Lager, UI & Erweiterungen (12.07.2026)



| Nr. | Leistung | Status |

|-----|----------|--------|

| 13 | **Lagerverwaltung** (63 Standard-Rohrprofile, Vollbild) | ✓ |

| 14 | Assistent: Profilart → **Stahl / Edelstahl / Aluminium** → Maße | ✓ |

| 15 | **Profilwahl zum Schneiden** mit Materialfilter (kein Stahl/Edelstahl-Mix) | ✓ |

| 16 | Lager-Reservierung für Auftrag + **Rohrest-Rückbuchung** ins Lager | ✓ |

| 17 | **Bestellliste-PDF** bei Fehlmenge (automatisch) | ✓ |

| 18 | Menüleiste (Datei, Lager, Einstellungen, Info) | ✓ |

| 19 | **PDF-Einstellungen** (Inhalte der Zuschnitt-PDF wählbar) | ✓ |

| 20 | Grafik: **komplette Stangenlänge** (App + PDF, kein Zoom) | ✓ |

| 21 | Farbiges UI (Buttons, Eingaben, Menü im Dunkel-/Hellmodus) | ✓ |

| 22 | Kunden-Produktbeschreibung & Arbeitsbericht | ✓ |

| 23 | **Manuelle Auftragsnummer** (Pflichtfeld, in PDFs & Dateinamen) | ✓ |

| 24 | **Auftragsverwaltung** (Wareneingang, Schnitt verbuchen, Status) | ✓ |



---



## 3. Arbeitszeitnachweis (Stunden)



**Ermittelt aus dem Cursor-Chat-Verlauf** (Nutzer-Nachrichten mit Zeitstempel). Gezählt wird nur **aktive Zeit ohne Pausen**: Unterbrechungen **länger als 20 Minuten** gelten als Pause.



### Phase 1 (10.–11.07.2026)



| Block | Von | Bis | Dauer |

|-------|-----|-----|------:|

| 1 | 10.07. 17:57 | 10.07. 18:05 | 0,13 h (8 Min) |

| 2 | 10.07. 18:43 | 10.07. 20:35 | 1,87 h (112 Min) |

| 3 | 10.07. 20:57 | 10.07. 21:34 | 0,62 h (37 Min) |

| 4 | 11.07. 09:34 | 11.07. 09:53 | 0,32 h (19 Min) |

| | | **Summe Phase 1** | **2,93 h ≈ 2,9 Std.** |



### Phase 2 (12.07.2026)



| Block | Von | Bis | Dauer |

|-------|-----|-----|------:|

| 5 | 12.07. 08:57 | 12.07. 11:02 | 2,08 h (125 Min) |

| | | **Summe Phase 2** | **2,08 h ≈ 2,1 Std.** |



| | | **Gesamtsumme** | **5,01 h ≈ 5,0 Std.** |



*Hinweis: Chat-Aktivitätszeit (Anforderungen, Testen, Screenshots). KI-Implementierung parallel in denselben Blöcken.*



---



## 4. Stundenlohn-Berechnung



### 4.1 Ansatz



Abrechnung nach **dokumentierter Arbeitszeit** × vereinbartem **Stundenlohn**.



### 4.2 Stundensätze (Richtwerte)



| Variante | Stundensatz | Anwendung |

|----------|------------:|-----------|

| Intern / Kostenstelle | 65 €/h | Eigenentwicklung |

| Standard Freelance | 85 €/h | Individualentwicklung DE |

| Spezialist WPF/Algorithmik | 95 €/h | Gehrung, Optimierung, Lagerverwaltung |



### 4.3 Gesamtberechnung



**Basis:** 5,0 Stunden (300 Min, Chat-Nachweis)



| Stundensatz | Netto (€) |

|------------:|----------:|

| 65 €/h | **325,00** |

| 85 €/h | **425,00** |

| 95 €/h | **475,00** |



**Beispiel (85 €/h):**



```

5,0 Std. × 85,00 €/h = 425,00 € (netto)

```



| Position | Betrag |

|----------|-------:|

| Phase 1 — Kern (2,9 Std.) | 246,50 € |

| Phase 2 — Lager & UI (2,1 Std.) | 178,50 € |

| **Zwischensumme netto** | **425,00 €** |

| USt. 19 % *(falls ausweisbar)* | 80,75 € |

| **Brutto** *(mit USt.)* | **505,75 €** |



*Bei Kleinunternehmerregelung § 19 UStG: Rechnung ohne USt.-Ausweis, Netto = Brutto.*



---



## 5. Projektwert & Betriebsnutzen



### 5.1 Entwicklungswert (Ersatzkosten)



Was die App **neu beauftragt** zu bauen kosten würde: **ca. 425–475 € netto** (5,0 Std. × 85–95 €/h).



### 5.2 Jährlicher Nutzen im Betrieb (Schätzung)



| Quelle | Ohne App | Mit App | Nutzen/Jahr |

|--------|----------|---------|------------:|

| **Planungszeit** | ~2 h/Woche manuell | ~10 Min/Woche | **~4.000 €** *(45 €/h × 2 h × 45 Wo.)* |

| **Material** | Reste vergessen, falsches Material | Lager, Rohreste, Materialfilter | **~500–1.500 €** |

| | | **Summe Nutzen** | **~4.500–5.500 €/Jahr** |



→ Amortisation der Entwicklungskosten (**~425 €**) in **unter 1 Monat** bei regelmäßiger Nutzung.



### 5.3 Markt- / Verkaufswert (Orientierung)



| Modell | Realistischer Rahmen |

|--------|---------------------|

| **Interner Einsatz** | Nutzen/Jahr **4.500–5.500 €** |

| **Einmal-Lizenz** (1 Werkstatt) | **500 €** *(Kollegenpreis, Entwickler arbeitet im Betrieb)*

| **Jahreslizenz + Support** | **400–800 €/Jahr** |



*Nische: Rohre mit Gehrung + Lagerverwaltung nach Materialart — wenig vergleichbare Standardsoftware.*



---



## 6. Vergleich: manuell vs. App



| Situation | Manuell | Mit App | Ersparnis |

|-----------|--------:|--------:|----------:|

| 8 Teile, Gehrungen, Lager + 1 Stange | ~30–45 Min | ~3 Min + PDF | **~25–40 Min** |

| Material aus Lager (Stahl/Edelstahl) | Fehlgriffe möglich | Assistent + Filter | **Weniger Verschnitt/Fehler** |

| Fehlmenge | Telefon/Notiz | Bestellliste-PDF | **Sofort dokumentiert** |



---



## 7. Lieferumfang



- Quellcode: `E:\Programierung\Rohre-Zuschnitt-Optimierung`

- Ausführbare Datei: `bin\Debug\net8.0-windows\RohreZuschnittOptimierung.exe`

- Lagerdaten: `%LOCALAPPDATA%\Rohre-Zuschnitt-Optimierung\pipe-warehouse.xml`

- Aufträge: `%LOCALAPPDATA%\Rohre-Zuschnitt-Optimierung\pipe-orders.xml`

- PDF-Ablage: `%LOCALAPPDATA%\Rohre-Zuschnitt-Optimierung\`

- Einstellungen: `pdf-export-settings.xml`, `theme.txt`

- Dokumentation: `README.md`, `PRODUKTBESCHREIBUNG_Kunde.md`, dieser Arbeitsbericht



---



## 8. Abnahme & Hinweise



- Die App ist ein **eigenständiges Produkt** (nicht Teil von DOK-V01).

- Nicht enthalten: Installer/USB-Release, Excel-Import, Mehrbenutzer-Lager, Cloud.

- Stunden = **reale Nutzer-Arbeitszeit** aus Chat-Nachweis.



---



## 9. Unterschrift / Freigabe



| | Auftraggeber | Entwickler |

|--|--------------|------------|

| Ort, Datum | _________________________ | Kruft, 12.07.2026 |

| Unterschrift | _________________________ | _________________________ |



---



*Arbeitsbericht Rohre Zuschnitt Optimierung · Stand 12.07.2026*

