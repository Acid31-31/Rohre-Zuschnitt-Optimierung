param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-RohreBuildRevision.ps1")
$buildInfo = Get-RohreRevisionFromProject -Root $root

$productName = "RohreZuschnittOptimierung"
$exeName = "$productName.exe"
$usbLauncherName = "Programm installieren.exe"
$productFolder = $buildInfo.ProductFolder
$projectFile = Join-Path $root "RohreZuschnittOptimierung.csproj"

Write-Host "[1/5] Release-Build ($Configuration)..."
dotnet build $projectFile -c $Configuration --no-incremental
if ($LASTEXITCODE -ne 0) {
    throw "Build fehlgeschlagen."
}

$releaseDir = Get-ChildItem (Join-Path $root "bin\$Configuration") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "net8.0-windows*" -and (Test-Path (Join-Path $_.FullName $exeName)) } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($releaseDir)) {
    throw "Release-Ausgabeordner nicht gefunden unter bin\$Configuration\net8.0-windows*"
}
$releaseExe = Join-Path $releaseDir $exeName
Write-Host "Release-Ordner: $releaseDir"
if (-not (Test-Path $releaseExe)) {
    throw "Release-EXE nicht gefunden: $releaseExe"
}

$signScript = Join-Path $root "sign-app.ps1"
. (Join-Path $root "Resolve-CodeSigningCert.ps1")
$certInfo = Resolve-CodeSigningCert -Root $root
if ((Test-Path $signScript) -and $certInfo.IsAvailable) {
    Write-Host "[1b/5] Release-EXE signieren..."
    & powershell -ExecutionPolicy Bypass -File $signScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Signieren fehlgeschlagen."
    }
}
else {
    Write-Warning "Signieren uebersprungen (Zertifikat nicht gefunden)."
}

$usbRoot = Join-Path $root "USB-Version"
$releaseRoot = Join-Path $root "Release-Version"
$appFolder = Join-Path $usbRoot $productFolder
$releaseFolder = Join-Path $releaseRoot $productFolder

Write-Host "[2/5] USB- und Release-Ordner vorbereiten ($productFolder)..."
if (-not (Test-Path $usbRoot)) {
    New-Item -ItemType Directory -Path $usbRoot | Out-Null
}
if (-not (Test-Path $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

# Nur den aktuellen Revisionsordner neu anlegen – ältere USB-Ordner nicht löschen,
# sonst brechen bestehende Desktop-Verknüpfungen (Ziel fehlt).
foreach ($target in @($appFolder, $releaseFolder)) {
    if (Test-Path $target) {
        try {
            Remove-Item $target -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Ordner gesperrt (Explorer offen?): $target"
        }
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

Write-Host "[3/5] Dateien kopieren..."
Get-ChildItem $releaseDir -File | Where-Object {
    $_.Extension -in @('.dll', '.config', '.json')
} | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $appFolder $_.Name) -Force
}

Get-ChildItem $releaseDir -File | Where-Object {
    $_.Extension -in @('.exe', '.dll', '.config', '.json')
} | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $releaseFolder $_.Name) -Force
}

$licenseSource = Join-Path $root "LICENSE_DE.txt"
if (Test-Path $licenseSource) {
    Copy-Item $licenseSource (Join-Path $appFolder "LICENSE_DE.txt") -Force
    Copy-Item $licenseSource (Join-Path $releaseFolder "LICENSE_DE.txt") -Force
}

Write-Host "[3b/5] Programmdateien fuer USB..."
Copy-Item $releaseExe (Join-Path $appFolder $exeName) -Force
Copy-Item $releaseExe (Join-Path $appFolder $usbLauncherName) -Force

$vendorAi = Join-Path $root "vendor\AI"
if (Test-Path (Join-Path $vendorAi "ollama\ollama.exe")) {
    Write-Host "[3b2/5] Mitgelieferte Vision-KI kopieren (AI\)..."
    $aiTargets = @(
        (Join-Path $appFolder "AI"),
        (Join-Path $releaseFolder "AI"),
        (Join-Path $releaseDir "AI")
    )
    foreach ($aiTarget in $aiTargets) {
        if (Test-Path $aiTarget) { Remove-Item $aiTarget -Recurse -Force -ErrorAction SilentlyContinue }
        New-Item -ItemType Directory -Path $aiTarget -Force | Out-Null
        # Copy-Item -Recurse hangs on ~1.8 GB model blobs; robocopy is reliable.
        & robocopy $vendorAi $aiTarget /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /MT:8 | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "Vision-KI Kopie fehlgeschlagen: $aiTarget (robocopy exit $LASTEXITCODE)"
        }
        # CUDA-Laufzeiten weglassen (Paketgröße); CPU/Vulkan reicht für Vision
        Get-ChildItem (Join-Path $aiTarget "ollama\lib\ollama") -Directory -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -like "cuda*" } |
          ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
else {
    Write-Warning "vendor\AI fehlt. Vor dem Release Prepare-BundledAi.ps1 ausfuehren (echte Vision-KI)."
}

$signPackageScript = Join-Path $root "sign-package-exes.ps1"
if ((Test-Path $signPackageScript) -and $certInfo.IsAvailable) {
    Write-Host "[3c/5] Signiere Paket-EXEs..."
    & powershell -ExecutionPolicy Bypass -File $signPackageScript -PackageRoot $appFolder
    if ($LASTEXITCODE -ne 0) {
        throw "Paket-Signierung fehlgeschlagen (USB)."
    }
    & powershell -ExecutionPolicy Bypass -File $signPackageScript -PackageRoot $releaseFolder
    if ($LASTEXITCODE -ne 0) {
        throw "Paket-Signierung fehlgeschlagen (Release)."
    }
}

$deployCer = Join-Path $root "CodeSigning.cer"
if (Test-Path $deployCer) {
    Copy-Item $deployCer (Join-Path $appFolder "CodeSigning.cer") -Force
    Copy-Item $deployCer (Join-Path $releaseFolder "CodeSigning.cer") -Force
}
elseif ($certInfo.PublicCerPath) {
    Copy-Item $certInfo.PublicCerPath (Join-Path $appFolder "CodeSigning.cer") -Force
    Copy-Item $certInfo.PublicCerPath (Join-Path $releaseFolder "CodeSigning.cer") -Force
}

$startBat = @"
@echo off
cd /d "%~dp0"
start "" "$exeName"
"@
Set-Content -Path (Join-Path $appFolder "STARTEN.bat") -Value $startBat -Encoding ASCII

$uninstallBat = @"
@echo off
cd /d "%~dp0"
start "" "$usbLauncherName" --uninstall
"@
Set-Content -Path (Join-Path $appFolder "DEINSTALLIEREN.bat") -Value $uninstallBat -Encoding ASCII

$readme = @"
Rohre Zuschnitt Optimierung $($buildInfo.RevisionLabel) - USB-Version (portabel)

SOFORT NUTZEN (ohne C:\Program Files):
1) Ordner "$productFolder" auf USB oder Desktop kopieren
2) $exeName oder STARTEN.bat starten -> Programm laeuft direkt aus dem Ordner

OPTIONAL EINRICHTEN (Assistent mit Lizenz + Desktop-Verknuepfung):
- $usbLauncherName doppelklicken
- Keine Administratorrechte noetig
- Programm bleibt im Ordner (portabel)

Arbeitsdaten: Ordner "Daten" neben der Programm-EXE (portabel, kein AppData)
Updates: automatisch von GitHub

Deinstallation:
- DEINSTALLIEREN.bat (entfernt Verknuepfung/Einrichtung, Ordner bleibt)
"@
Set-Content -Path (Join-Path $appFolder "README_USB.txt") -Value $readme -Encoding UTF8

Write-Host "[4/5] ZIP-Archive erstellen..."
$usbZipPath = Join-Path $usbRoot $buildInfo.UsbZipName
$releaseZipPath = Join-Path $releaseRoot $buildInfo.ReleaseZipName
foreach ($zip in @($usbZipPath, $releaseZipPath)) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
}
Compress-Archive -Path (Join-Path $appFolder "*") -DestinationPath $usbZipPath -Force
Compress-Archive -Path (Join-Path $releaseFolder "*") -DestinationPath $releaseZipPath -Force

Write-Host "[5/5] Kopie auf Z: ($productFolder)..."
$zFolder = Join-Path 'Z:\' $productFolder
$zZip = Join-Path 'Z:\' $buildInfo.UsbZipName
if (Test-Path 'Z:\') {
    New-Item -ItemType Directory -Path $zFolder -Force | Out-Null
    & robocopy $appFolder $zFolder /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD Daten | Out-Null
    Copy-Item $usbZipPath $zZip -Force
    Write-Host "Z-Ordner: $zFolder"
    Write-Host "Z-ZIP:    $zZip"
}
else {
    Write-Warning "Laufwerk Z: nicht gefunden – Kopie uebersprungen."
}

Write-Host ""
Write-Host "Fertig - $($buildInfo.RevisionLabel)"
Write-Host "USB-Ordner:     $appFolder"
Write-Host "USB-ZIP:        $usbZipPath"
Write-Host "Release-Ordner: $releaseFolder"
Write-Host "Release-ZIP:    $releaseZipPath"
Write-Host ""

explorer.exe $appFolder
explorer.exe $releaseFolder
if (Test-Path $zFolder) { explorer.exe $zFolder }
