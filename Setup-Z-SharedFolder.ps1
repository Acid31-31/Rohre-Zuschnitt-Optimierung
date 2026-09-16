# Gemeinsamer Freigabe-Ordner auf Z:
#   Z:\Rohre-Zuschnitt\
#     App-Dateien (aktuelle Revision, standalone)
#     AI\          <- einmalig / bei KI-Update aktualisieren
#     Pakete\      <- aktuelle ZIP(s)
#     VERSION.txt
#     STARTEN.bat
#     README.txt

$ErrorActionPreference = "Stop"
$share = "Z:\Rohre-Zuschnitt"
$root = "Z:\Programierung\Rohre-Zuschnitt-Optimierung"
$appSource = Join-Path $root "USB-Version\Rohre-Zuschnitt-R23"
$releaseZip = Join-Path $root "Release-Version\RohreZuschnittOptimierung-Release-R23.zip"
$usbZip = Join-Path $root "USB-Version\Rohre-Zuschnitt-R23.zip"
$aiSourceCandidates = @(
  (Join-Path $share "AI"),
  "Z:\Rohre-Zuschnitt-R22\AI",
  "Z:\Rohre-Zuschnitt-R21\AI",
  (Join-Path $root "vendor\AI")
)

if (-not (Test-Path $appSource)) { throw "App-Quelle fehlt: $appSource" }
if (-not (Test-Path $releaseZip)) { throw "Release-ZIP fehlt: $releaseZip" }

Write-Host "=== Gemeinsamen Ordner vorbereiten: $share ==="
New-Item -ItemType Directory -Path $share -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $share "Pakete") -Force | Out-Null

# AI zuerst sichern/platzieren (nicht beim App-Sync loeschen)
$aiDest = Join-Path $share "AI"
$aiReady = Test-Path (Join-Path $aiDest "ollama\ollama.exe")
if (-not $aiReady) {
  $aiSrc = $aiSourceCandidates | Where-Object {
    $_ -ne $aiDest -and (Test-Path (Join-Path $_ "ollama\ollama.exe"))
  } | Select-Object -First 1
  if (-not $aiSrc) { throw "Keine AI-Quelle gefunden." }
  Write-Host "AI nach $aiDest (Quelle: $aiSrc)..."
  if ($aiSrc.StartsWith("Z:\Rohre-Zuschnitt-R", [StringComparison]::OrdinalIgnoreCase) -and (Test-Path $aiSrc)) {
    # Gleiches Laufwerk: Verschieben ist schnell
    if (Test-Path $aiDest) { Remove-Item $aiDest -Recurse -Force -ErrorAction SilentlyContinue }
    Move-Item -Path $aiSrc -Destination $aiDest
  }
  else {
    New-Item -ItemType Directory -Path $aiDest -Force | Out-Null
    & robocopy $aiSrc $aiDest /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /MT:8 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "AI-Kopie fehlgeschlagen ($LASTEXITCODE)" }
  }
}
else {
  Write-Host "AI bereits vorhanden - belassen."
}

Write-Host "App R23 nach $share (AI und Daten bleiben)..."
& robocopy $appSource $share /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD AI Daten Pakete | Out-Null
if ($LASTEXITCODE -ge 8) { throw "App-Kopie fehlgeschlagen ($LASTEXITCODE)" }

Write-Host "ZIPs nach Pakete\..."
Copy-Item $releaseZip (Join-Path $share "Pakete\RohreZuschnittOptimierung-Release-R23.zip") -Force
Copy-Item $usbZip (Join-Path $share "Pakete\Rohre-Zuschnitt-R23.zip") -Force
Copy-Item $releaseZip (Join-Path $share "Pakete\RohreZuschnittOptimierung-Release-AKTUELL.zip") -Force

@"
R23
"@ | Set-Content -Path (Join-Path $share "VERSION.txt") -Encoding ASCII

@"
@echo off
cd /d "%~dp0"
start "" "RohreZuschnittOptimierung.exe"
"@ | Set-Content -Path (Join-Path $share "STARTEN.bat") -Encoding ASCII

@"
Rohre Zuschnitt - gemeinsamer Freigabe-Ordner (Z:\Rohre-Zuschnitt)

Inhalt:
- Programm (aktuelle Revision, standalone - kein extra .NET noetig)
- AI\          Vision-KI (separat; bleibt bei App-Updates erhalten)
- Pakete\      aktuelle ZIP fuer USB/GitHub
- VERSION.txt  aktuelle Revision
- STARTEN.bat

Nutzen auf anderem PC:
1) Diesen Ordner nutzen oder auf Desktop kopieren
2) STARTEN.bat oder RohreZuschnittOptimierung.exe
3) Online-Update aktualisiert nur die App - AI\ und Daten\ bleiben

KI aktualisieren:
- Inhalt von AI\ ersetzen/aktualisieren (vendor\AI bzw. neues KI-Paket)
- App-Ordner nicht anfassen

Nicht mehr verwenden:
- Alte Ordner Rohre-Zuschnitt-R19 ... R22 auf Z:\ (werden entfernt)
"@ | Set-Content -Path (Join-Path $share "README.txt") -Encoding UTF8

$ver = (Get-Content (Join-Path $share "VERSION.txt") -Raw).Trim()
Write-Host "Fertig. VERSION=$ver"
Write-Host "EXE=$(Test-Path (Join-Path $share 'RohreZuschnittOptimierung.exe')) AI=$(Test-Path (Join-Path $aiDest 'ollama\ollama.exe'))"
