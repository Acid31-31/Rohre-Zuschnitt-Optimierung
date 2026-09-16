param(
    [string]$Configuration = "Release",
    [switch]$IncludeAi
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

# Feste Struktur (USB = gleicher Inhalt, anderer Name im Projekt: USB-Version):
# Z:\Rohre-Zuschnitt\
#   Rohre-Zuschnitt-Rxx\
#   Rohre-Zuschnitt-Rxx.zip
#   AI\                 (optional, separat)
$zParent = 'Z:\Rohre-Zuschnitt'
$zRevFolder = Join-Path $zParent $productFolder
$zRevZip = Join-Path $zParent $buildInfo.UsbZipName

$projectUsbRoot = Join-Path $root 'USB-Version'
$projectRevFolder = Join-Path $projectUsbRoot $productFolder
$projectRevZip = Join-Path $projectUsbRoot $buildInfo.UsbZipName

$backupRoot = 'Z:\Programierung\Rohre-Zuschnitt-Absicherung'
$backupProgram = Join-Path $backupRoot 'Programm'

Write-Host "[1/5] Self-contained Publish ($Configuration, win-x64)..."
$publishDir = Join-Path $root "publish\win-x64-sc"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}
dotnet publish $projectFile -c $Configuration -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "Publish fehlgeschlagen."
}

$releaseExe = Join-Path $publishDir $exeName
if (-not (Test-Path $releaseExe)) {
    throw "Release-EXE nicht gefunden: $releaseExe"
}

$signScript = Join-Path $root "sign-app.ps1"
. (Join-Path $root "Resolve-CodeSigningCert.ps1")
$certInfo = Resolve-CodeSigningCert -Root $root
if ((Test-Path $signScript) -and $certInfo.IsAvailable) {
    Write-Host "Signiere Release-EXE..."
    & powershell -ExecutionPolicy Bypass -File $signScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Signieren fehlgeschlagen."
    }
}
else {
    Write-Warning "Signieren uebersprungen (Zertifikat nicht gefunden)."
}

function Add-UsbLaunchFiles {
    param([string]$TargetRoot)

    $licenseSource = Join-Path $root "LICENSE_DE.txt"
    if (Test-Path $licenseSource) {
        Copy-Item $licenseSource (Join-Path $TargetRoot "LICENSE_DE.txt") -Force
    }

    Copy-Item $releaseExe (Join-Path $TargetRoot $usbLauncherName) -Force

    $deployCer = Join-Path $root "CodeSigning.cer"
    if (Test-Path $deployCer) {
        Copy-Item $deployCer (Join-Path $TargetRoot "CodeSigning.cer") -Force
    }
    elseif ($certInfo.PublicCerPath) {
        Copy-Item $certInfo.PublicCerPath (Join-Path $TargetRoot "CodeSigning.cer") -Force
    }

    $startBat = @"
@echo off
cd /d "%~dp0"
start "" "$exeName"
"@
    Set-Content -Path (Join-Path $TargetRoot "STARTEN.bat") -Value $startBat -Encoding ASCII

    $uninstallBat = @"
@echo off
cd /d "%~dp0"
start "" "$usbLauncherName" --uninstall
"@
    Set-Content -Path (Join-Path $TargetRoot "DEINSTALLIEREN.bat") -Value $uninstallBat -Encoding ASCII

    $readme = @"
Rohre Zuschnitt Optimierung $($buildInfo.RevisionLabel) - USB-Version (standalone)

STARTEN: $exeName oder STARTEN.bat
EINRICHTEN: $usbLauncherName
DEINSTALLIEREN: DEINSTALLIEREN.bat
Vision-KI: Ordner AI\ neben die EXE (nicht im Paket)
"@
    Set-Content -Path (Join-Path $TargetRoot "README_USB.txt") -Value $readme -Encoding UTF8

    $diagnoseSource = Join-Path $root "Diagnose-Start.bat"
    if (Test-Path $diagnoseSource) {
        Copy-Item $diagnoseSource (Join-Path $TargetRoot "Diagnose-Start.bat") -Force
    }
}

function Deploy-Package {
    param([string]$TargetRoot)

    if (Test-Path $TargetRoot) {
        Remove-Item $TargetRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
    & robocopy $publishDir $TargetRoot /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XF "*.pdb" /XD Daten | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Kopie nach $TargetRoot fehlgeschlagen (robocopy exit $LASTEXITCODE)"
    }
    Add-UsbLaunchFiles -TargetRoot $TargetRoot
}

function New-PackageZip {
    param(
        [string]$SourceFolder,
        [string]$ZipPath
    )

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $SourceFolder '*') -DestinationPath $ZipPath -Force
}

if (-not (Test-Path 'Z:\')) {
    throw "Laufwerk Z: nicht gefunden."
}

Write-Host "[2/5] Projekt USB-Version: $projectUsbRoot"
New-Item -ItemType Directory -Path $projectUsbRoot -Force | Out-Null
Deploy-Package -TargetRoot $projectRevFolder
New-PackageZip -SourceFolder $projectRevFolder -ZipPath $projectRevZip

Write-Host "[3/5] Z-Freigabe: $zParent"
New-Item -ItemType Directory -Path $zParent -Force | Out-Null
Deploy-Package -TargetRoot $zRevFolder
New-PackageZip -SourceFolder $zRevFolder -ZipPath $zRevZip

if ($IncludeAi) {
    $vendorAi = Join-Path $root "vendor\AI"
    $aiTarget = Join-Path $zParent 'AI'
    if (Test-Path (Join-Path $vendorAi "ollama\ollama.exe")) {
        Write-Host "Vision-KI nach $aiTarget kopieren..."
        New-Item -ItemType Directory -Path $aiTarget -Force | Out-Null
        & robocopy $vendorAi $aiTarget /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /MT:8 | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "Vision-KI Kopie fehlgeschlagen."
        }
    }
    else {
        Write-Warning "vendor\AI fehlt."
    }
}

$signPackageScript = Join-Path $root "sign-package-exes.ps1"
if ((Test-Path $signPackageScript) -and $certInfo.IsAvailable) {
    foreach ($pkg in @($projectRevFolder, $zRevFolder)) {
        & powershell -ExecutionPolicy Bypass -File $signPackageScript -PackageRoot $pkg
        if ($LASTEXITCODE -ne 0) {
            throw "Paket-Signierung fehlgeschlagen: $pkg"
        }
    }
}

Write-Host "[4/5] Absicherung: $backupRoot"
New-Item -ItemType Directory -Path $backupProgram -Force | Out-Null
& robocopy $zRevFolder $backupProgram /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD Daten | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Absicherung Programm fehlgeschlagen (robocopy exit $LASTEXITCODE)"
}

$sourceZipName = "Rohre-Zuschnitt-Quelle-$($buildInfo.RevisionLabel).zip"
$sourceZipDest = Join-Path $backupRoot $sourceZipName
$stage = Join-Path $env:TEMP "Rohre-Zuschnitt-Quelle-$($buildInfo.RevisionLabel)"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$excludeDirNames = @(
    ".git", ".vs", "bin", "obj", "USB-Version", "Release-Version",
    "publish", "Test-Update", "vendor", "Logos"
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

Get-ChildItem $backupRoot -Filter "Rohre-Zuschnitt-Quelle-*.zip" |
    Where-Object { $_.Name -ne $sourceZipName } |
    ForEach-Object { Remove-Item $_.FullName -Force }

$readmeLines = @(
    "Rohre Zuschnitt Optimierung - Absicherung $($buildInfo.RevisionLabel)"
    "Stand: $(Get-Date -Format 'dd.MM.yyyy HH:mm')"
    ""
    "PROGRAMM: Programm\STARTEN.bat"
    "ORDNER: $zRevFolder"
    "ZIP: $zRevZip"
    "QUELLCODE: $sourceZipName"
)
Set-Content -Path (Join-Path $backupRoot "README_SICHERUNG.txt") -Value $readmeLines -Encoding UTF8

Write-Host "[5/5] Alte Revisionen aufraeumen (nur aktuelle behalten)..."
foreach ($cleanupRoot in @($projectUsbRoot, $zParent)) {
    if (-not (Test-Path $cleanupRoot)) { continue }
    Get-ChildItem $cleanupRoot -Force | Where-Object {
        ($_.PSIsContainer -and $_.Name -like 'Rohre-Zuschnitt-R*' -and $_.Name -ne $productFolder) `
        -or ($_.Name -like 'Rohre-Zuschnitt-R*.zip' -and $_.Name -ne $buildInfo.UsbZipName) `
        -or ($_.Name -like 'Rohre-Zuschnitt_R*') `
        -or ($_.Name -eq 'USB')
    } | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Entfernt: $($_.FullName)"
    }
}

# Lose Dateien im Z-Hauptordner entfernen (alte flache Struktur), ZIP behalten
Get-ChildItem $zParent -File -Force -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -ne $buildInfo.UsbZipName
} | ForEach-Object {
    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
    Write-Host "  Entfernt (lose Datei): $($_.FullName)"
}

if (Test-Path (Join-Path 'Z:\' $buildInfo.UsbZipName)) {
    Remove-Item (Join-Path 'Z:\' $buildInfo.UsbZipName) -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Fertig - $($buildInfo.RevisionLabel)"
Write-Host "Z-Hauptordner: $zParent"
Write-Host "  $productFolder\"
Write-Host "  $($buildInfo.UsbZipName)"
Write-Host "Projekt:       $projectUsbRoot"
Write-Host "Absicherung:   $backupRoot"
Write-Host ""

explorer.exe $zParent
