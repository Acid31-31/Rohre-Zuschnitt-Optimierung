# Struktur auf Z:\Rohre-Zuschnitt:
#   Aktuelle-Revision\   Programmdateien (ohne AI)
#   Aktuelle-ZIP\        aktuelle ZIP-Dateien
#   AI\                  Vision-KI (separat)
#   USB\                 fertig zum Kopieren auf USB-Stick (Revision + AI)

$ErrorActionPreference = "Stop"
$share = "Z:\Rohre-Zuschnitt"
$revDir = Join-Path $share "Aktuelle-Revision"
$zipDir = Join-Path $share "Aktuelle-ZIP"
$aiDir = Join-Path $share "AI"
$usbDir = Join-Path $share "USB"
$root = "Z:\Programierung\Rohre-Zuschnitt-Optimierung"
$appSource = Join-Path $root "USB-Version\Rohre-Zuschnitt-R23"
$releaseZip = Join-Path $root "Release-Version\RohreZuschnittOptimierung-Release-R23.zip"
$usbZip = Join-Path $root "USB-Version\Rohre-Zuschnitt-R23.zip"

if (-not (Test-Path $appSource)) { throw "App-Quelle fehlt: $appSource" }

Write-Host "Ordner anlegen..."
New-Item -ItemType Directory -Path $revDir,$zipDir,$usbDir -Force | Out-Null

# Falls App noch lose im Root liegt: nach Aktuelle-Revision verschieben
$rootExe = Join-Path $share "RohreZuschnittOptimierung.exe"
if (Test-Path $rootExe) {
  Write-Host "Lose App-Dateien aus Root nach Aktuelle-Revision verschieben..."
  Get-ChildItem $share -Force | Where-Object {
    $_.Name -notin @("Aktuelle-Revision","Aktuelle-ZIP","AI","USB","Pakete","VERSION.txt","README.txt","STARTEN.bat","LIESMICH.txt")
  } | ForEach-Object {
    $dest = Join-Path $revDir $_.Name
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue }
    Move-Item $_.FullName $dest -Force
  }
}

Write-Host "Aktuelle-Revision aus Build aktualisieren (AI bleibt unangetastet)..."
& robocopy $appSource $revDir /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD AI Daten | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Revision-Kopie fehlgeschlagen ($LASTEXITCODE)" }

# Alte Pakete\ nach Aktuelle-ZIP
$oldPakete = Join-Path $share "Pakete"
if (Test-Path $oldPakete) {
  Get-ChildItem $oldPakete -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $zipDir $_.Name) -Force
  }
  Remove-Item $oldPakete -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $releaseZip) {
  Copy-Item $releaseZip (Join-Path $zipDir "RohreZuschnittOptimierung-Release-R23.zip") -Force
  Copy-Item $releaseZip (Join-Path $zipDir "RohreZuschnittOptimierung-Release-AKTUELL.zip") -Force
}
if (Test-Path $usbZip) {
  Copy-Item $usbZip (Join-Path $zipDir "Rohre-Zuschnitt-R23.zip") -Force
  Copy-Item $usbZip (Join-Path $zipDir "Rohre-Zuschnitt-USB-AKTUELL.zip") -Force
}

if (-not (Test-Path (Join-Path $aiDir "ollama\ollama.exe"))) {
  $aiSrc = Join-Path $root "vendor\AI"
  if (-not (Test-Path (Join-Path $aiSrc "ollama\ollama.exe"))) {
    throw "AI fehlt unter $aiDir und $aiSrc"
  }
  Write-Host "AI aus vendor kopieren..."
  New-Item -ItemType Directory -Path $aiDir -Force | Out-Null
  & robocopy $aiSrc $aiDir /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /MT:8 | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "AI-Kopie fehlgeschlagen ($LASTEXITCODE)" }
}
else {
  Write-Host "AI bereits vorhanden."
}

Write-Host "USB-Ordner bauen (Revision + AI-Verknuepfung, ohne Doppelkopie)..."
if (Test-Path $usbDir) {
  Get-ChildItem $usbDir -Force | Where-Object { $_.Name -ne "Daten" } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $usbDir -Force | Out-Null
& robocopy $revDir $usbDir /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD Daten AI | Out-Null
if ($LASTEXITCODE -ge 8) { throw "USB-App-Kopie fehlgeschlagen ($LASTEXITCODE)" }
$usbAi = Join-Path $usbDir "AI"
if (Test-Path $usbAi) { cmd /c "rmdir /s /q `"$usbAi`"" | Out-Null }
cmd /c "mklink /J `"$usbAi`" `"$aiDir`"" | Out-Null
if (-not (Test-Path (Join-Path $usbAi "ollama\ollama.exe"))) {
  throw "USB\AI Junction fehlgeschlagen"
}

@"
R23
"@ | Set-Content (Join-Path $share "VERSION.txt") -Encoding ASCII

@"
Rohre Zuschnitt - Freigabe auf Z:

Ordner:
  Aktuelle-Revision\   nur Programm (ohne AI)
  Aktuelle-ZIP\        ZIP-Dateien (USB + GitHub-Update)
  AI\                  Vision-KI separat (bei KI-Update nur hier aktualisieren)
  USB\                 fertig zum Kopieren auf USB-Stick (Revision + AI)

Start lokal:
  USB\STARTEN.bat   oder   Aktuelle-Revision\ + AI\ manuell

Online-Update aktualisiert die App-Dateien; AI bleibt erhalten, wenn AI neben der EXE liegt.
"@ | Set-Content (Join-Path $share "README.txt") -Encoding UTF8

Write-Host "Fertig."
Write-Host "Revision EXE=$(Test-Path (Join-Path $revDir 'RohreZuschnittOptimierung.exe'))"
Write-Host "AI=$(Test-Path (Join-Path $aiDir 'ollama\ollama.exe'))"
Write-Host "USB EXE=$(Test-Path (Join-Path $usbDir 'RohreZuschnittOptimierung.exe')) USB AI=$(Test-Path (Join-Path $usbAi 'ollama\ollama.exe'))"
Get-ChildItem $share | Select-Object Name,Mode | Format-Table -AutoSize
Get-ChildItem $zipDir | Select-Object Name,@{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
