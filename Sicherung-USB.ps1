param(
    [string]$DestinationRoot = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-RohreBuildRevision.ps1")
$buildInfo = Get-RohreRevisionFromProject -Root $root

function Get-UsbBackupDrive {
    param([string]$PreferredRoot)

    if (-not [string]::IsNullOrWhiteSpace($PreferredRoot)) {
        if (-not (Test-Path $PreferredRoot)) {
            throw "Zielpfad nicht gefunden: $PreferredRoot"
        }
        return (Get-Item $PreferredRoot).FullName
    }

    $removable = Get-Volume | Where-Object {
        $_.DriveLetter -and $_.DriveType -eq "Removable"
    } | Sort-Object SizeRemaining -Descending

    if (-not $removable) {
        throw "Kein USB-Stick gefunden. Stick einstecken und erneut ausfuehren."
    }

    $drive = $removable | Select-Object -First 1
    $letter = $drive.DriveLetter
    $freeGb = [math]::Round($drive.SizeRemaining / 1GB, 2)
    Write-Host "USB-Stick: ${letter}:  (frei ${freeGb} GB)"
    return "${letter}:\"
}

$usbDrive = Get-UsbBackupDrive -PreferredRoot $DestinationRoot
$backupRoot = Join-Path $usbDrive "Rohre-Zuschnitt-Optimierung"
$programDest = Join-Path $backupRoot "Programm"
$sourceZipName = "Rohre-Zuschnitt-Quelle-$($buildInfo.RevisionLabel).zip"
$sourceZipDest = Join-Path $backupRoot $sourceZipName

$usbSource = Join-Path $root "USB-Version\$($buildInfo.ProductFolder)"
if (-not (Test-Path (Join-Path $usbSource "RohreZuschnittOptimierung.exe"))) {
    throw "USB-Programm fehlt: $usbSource`nZuerst .\create-usb-version.ps1 ausfuehren."
}

Write-Host "[1/3] Sicherungsordner anlegen: $backupRoot"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

Write-Host "[2/3] Programm kopieren ($($buildInfo.RevisionLabel))..."
if (Test-Path $programDest) {
    cmd /c "rmdir /s /q `"$programDest`"" | Out-Null
}
New-Item -ItemType Directory -Path $programDest -Force | Out-Null
& robocopy $usbSource $programDest /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Kopieren des Programms fehlgeschlagen (robocopy $LASTEXITCODE)."
}

Write-Host "[3/3] Quellcode-ZIP erstellen..."
$stage = Join-Path $env:TEMP "Rohre-Zuschnitt-Quelle-$($buildInfo.RevisionLabel)"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$excludeDirNames = @(
    ".git", ".vs", "bin", "obj", "USB-Version", "Release-Version",
    "Test-Update", "vendor", "Logos"
)
Get-ChildItem $root -Force | Where-Object {
    $_.Name -notin $excludeDirNames
} | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $stage $_.Name) -Recurse -Force
}

$tempZip = Join-Path $env:TEMP $sourceZipName
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $tempZip -Force
Copy-Item $tempZip $sourceZipDest -Force
Remove-Item $stage -Recurse -Force
Remove-Item $tempZip -Force

$readme = @"
Rohre Zuschnitt Optimierung - Absicherung $($buildInfo.RevisionLabel)
Stand: $(Get-Date -Format "dd.MM.yyyy HH:mm")

PROGRAMM (sofort nutzbar):
  Ordner "Programm" auf den PC kopieren oder direkt vom Stick starten:
  Programm\STARTEN.bat
  oder Programm\RohreZuschnittOptimierung.exe

QUELLCODE (Wiederherstellung):
  $sourceZipName  - Projekt ohne Build-Ordner und ohne KI-Paket

GITHUB:
  https://github.com/Acid31-31/Rohre-Zuschnitt-Optimierung

Diese Sicherung nicht mit den Ordnern "DOK-V01 Soft" oder "Privat" vermischen.
"@
Set-Content -Path (Join-Path $backupRoot "README_SICHERUNG.txt") -Value $readme -Encoding UTF8

Write-Host ""
Write-Host "Absicherung fertig: $backupRoot"
Write-Host "Programm:  $programDest"
Write-Host "Quelle:    $sourceZipDest"
explorer.exe $backupRoot
