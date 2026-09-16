param(
    [string]$DestinationRoot = ''
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-RohreBuildRevision.ps1")
$buildInfo = Get-RohreRevisionFromProject -Root $root

$zParent = 'Z:\Rohre-Zuschnitt'
$revFolder = Join-Path $zParent $buildInfo.ProductFolder

$programSource = $revFolder
if (-not (Test-Path (Join-Path $programSource "RohreZuschnittOptimierung.exe"))) {
    $programSource = Join-Path $root ("USB-Version\" + $buildInfo.ProductFolder)
}
if (-not (Test-Path (Join-Path $programSource "RohreZuschnittOptimierung.exe"))) {
    throw "USB-Programm fehlt. Zuerst create-usb-version.ps1 ausfuehren."
}

$backupRoot = if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    'Z:\Programierung\Rohre-Zuschnitt-Absicherung'
} else {
    Join-Path $DestinationRoot 'Rohre-Zuschnitt-Absicherung'
}

$backupProgram = Join-Path $backupRoot 'Programm'
Write-Host "Absicherung nach: $backupRoot"
New-Item -ItemType Directory -Path $backupProgram -Force | Out-Null

& robocopy $programSource $backupProgram /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD AI Daten | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Kopieren fehlgeschlagen (robocopy $LASTEXITCODE)."
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
    "QUELLCODE: $sourceZipName"
    "ORDNER: $revFolder"
)
Set-Content -Path (Join-Path $backupRoot "README_SICHERUNG.txt") -Value $readmeLines -Encoding UTF8

Write-Host "Fertig: $backupRoot"
explorer.exe $backupRoot
