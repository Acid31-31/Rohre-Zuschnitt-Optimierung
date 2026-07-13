param(
  [string]$Tag = "",
  [string]$Configuration = "Release",
  [switch]$SkipBuild,
  [switch]$SkipGh,
  [switch]$Draft
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$productName = "RohreZuschnittOptimierung"
$assetName = "RohreZuschnittOptimierung-Release.zip"
$projectFile = Join-Path $root "RohreZuschnittOptimierung.csproj"
$releaseRoot = Join-Path $root "Release-Version"

$csproj = [xml](Get-Content $projectFile)
$versionNode = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
$version = if ($versionNode) { "$versionNode" } else { "1.0.0" }
if ([string]::IsNullOrWhiteSpace($Tag)) {
  $Tag = "v$version"
}

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
  $msbuild = "dotnet"
}

if (-not $SkipBuild) {
  Write-Host "Baue $Configuration..."
  if ($msbuild -eq "dotnet") {
    dotnet build $projectFile -c $Configuration
  } else {
    & $msbuild $projectFile /t:Rebuild /p:Configuration=$Configuration
  }
  if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }
}

$releaseDir = Join-Path $root "bin\$Configuration\net8.0-windows"
$exePath = Join-Path $releaseDir "$productName.exe"
if (-not (Test-Path $exePath)) {
  throw "Release-EXE nicht gefunden: $exePath"
}

$staging = Join-Path $env:TEMP "RohreZuschnitt-release-staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

Get-ChildItem $releaseDir -File | Where-Object {
  $_.Extension -in @('.exe', '.dll', '.config', '.json', '.cer')
} | ForEach-Object {
  Copy-Item $_.FullName -Destination (Join-Path $staging $_.Name) -Force
}

$releaseFolder = Join-Path $releaseRoot "RohreZuschnittOptimierung-$version"
if (-not (Test-Path $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
if (Test-Path $releaseFolder) { Remove-Item $releaseFolder -Recurse -Force }
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
Copy-Item (Join-Path $staging "*") $releaseFolder -Recurse -Force

$zipPath = Join-Path $releaseRoot $assetName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$notes = @"
Rohre Zuschnitt Optimierung $Tag

SHA256: $hash

- Automatische Updates über GitHub
- Lagerverwaltung und Auftragsführung
"@

Write-Host ""
Write-Host "Release-Paket: $zipPath"
Write-Host "SHA256: $hash"
Write-Host "Tag: $Tag"
Write-Host ""

if ($SkipGh) {
  Write-Host "GitHub-Upload übersprungen (-SkipGh)."
  exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
  Write-Warning "gh CLI nicht gefunden. ZIP manuell auf GitHub hochladen."
  exit 0
}

$releaseArgs = @("release", "create", $Tag, $zipPath, "--title", "Rohre Zuschnitt Optimierung $Tag", "--notes", $notes)
if ($Draft) { $releaseArgs += "--draft" }

& gh @releaseArgs
if ($LASTEXITCODE -ne 0) {
  throw "GitHub-Release fehlgeschlagen."
}

Write-Host "GitHub-Release erstellt: $Tag"
