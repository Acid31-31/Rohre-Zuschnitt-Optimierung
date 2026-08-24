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

. (Join-Path $root "Get-RohreBuildRevision.ps1")
. (Join-Path $root "Get-RohreReleaseNotes.ps1")
$buildInfo = Get-RohreRevisionFromProject -Root $root

$productName = "RohreZuschnittOptimierung"
$assetName = $buildInfo.ReleaseZipName
$projectFile = Join-Path $root "RohreZuschnittOptimierung.csproj"
$releaseRoot = Join-Path $root "Release-Version"
$releaseFolder = Join-Path $releaseRoot $buildInfo.ProductFolder

if ([string]::IsNullOrWhiteSpace($Tag)) {
  $Tag = $buildInfo.VersionTag
}

if (-not $SkipBuild) {
  Write-Host "Baue $Configuration ($($buildInfo.RevisionLabel))..."
  dotnet build $projectFile -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }

  $signScript = Join-Path $root "sign-app.ps1"
  . (Join-Path $root "Resolve-CodeSigningCert.ps1")
  $certInfo = Resolve-CodeSigningCert -Root $root
  if ((Test-Path $signScript) -and $certInfo.IsAvailable) {
    Write-Host "Signiere Release-EXE..."
    & powershell -ExecutionPolicy Bypass -File $signScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Signieren fehlgeschlagen." }
  }
  else {
    Write-Warning "Signieren uebersprungen (Zertifikat nicht gefunden)."
  }
}

$releaseDir = Get-ChildItem (Join-Path $root "bin\$Configuration") -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like "net8.0-windows*" -and (Test-Path (Join-Path $_.FullName "$productName.exe")) } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($releaseDir)) {
  throw "Release-Ausgabeordner nicht gefunden unter bin\$Configuration\net8.0-windows*"
}
$exePath = Join-Path $releaseDir "$productName.exe"
Write-Host "Release-Ordner: $releaseDir"
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

$cerSource = Join-Path $releaseDir "CodeSigning.cer"
if (-not (Test-Path $cerSource)) {
  $cerSource = Join-Path $root "CodeSigning.cer"
}
if (-not (Test-Path $cerSource)) {
  if (-not $certInfo) {
    . (Join-Path $root "Resolve-CodeSigningCert.ps1")
    $certInfo = Resolve-CodeSigningCert -Root $root
  }
  $cerSource = $certInfo.PublicCerPath
}
if ($cerSource -and (Test-Path $cerSource)) {
  Copy-Item $cerSource (Join-Path $staging "CodeSigning.cer") -Force
  Write-Host "CodeSigning.cer: $cerSource"
}

if (-not (Test-Path $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
if (Test-Path $releaseFolder) { Remove-Item $releaseFolder -Recurse -Force }
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
Copy-Item (Join-Path $staging "*") $releaseFolder -Recurse -Force

$vendorAi = Join-Path $root "vendor\AI"
if (Test-Path (Join-Path $vendorAi "ollama\ollama.exe")) {
  Write-Host "Kopiere mitgelieferte Vision-KI (AI\)..."
  $aiDest = Join-Path $releaseFolder "AI"
  if (Test-Path $aiDest) { Remove-Item $aiDest -Recurse -Force }
  New-Item -ItemType Directory -Path $aiDest -Force | Out-Null
  Copy-Item (Join-Path $vendorAi "*") -Destination $aiDest -Recurse -Force
  Get-ChildItem (Join-Path $aiDest "ollama\lib\ollama") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "cuda*" } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
  # AI auch ins ZIP-Staging
  $aiStaging = Join-Path $staging "AI"
  if (Test-Path $aiStaging) { Remove-Item $aiStaging -Recurse -Force }
  Copy-Item $aiDest -Destination $aiStaging -Recurse -Force
}
else {
  Write-Warning "vendor\AI fehlt – Release ohne Vision-KI-Paket."
}

$zipPath = Join-Path $releaseRoot $assetName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$changeItems = Get-RohreReleaseNotes -Root $root -RevisionLabel $buildInfo.RevisionLabel
$changeLines = ($changeItems | ForEach-Object { "- $_" }) -join "`n"
$notes = @"
Rohre Zuschnitt Optimierung $Tag ($($buildInfo.RevisionLabel))

Aenderungen:
$changeLines

SHA256: $hash
"@

Write-Host ""
Write-Host "Release-Paket: $zipPath"
Write-Host "Release-Ordner: $releaseFolder"
Write-Host "SHA256: $hash"
Write-Host "Tag: $Tag"
Write-Host ""

if ($SkipGh) {
  Write-Host "GitHub-Upload übersprungen (-SkipGh)."
  explorer.exe $releaseFolder
  exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
  Write-Warning "gh CLI nicht gefunden. ZIP manuell auf GitHub hochladen."
  explorer.exe $releaseFolder
  exit 0
}

$releaseArgs = @("release", "create", $Tag, $zipPath, "--title", "Rohre Zuschnitt Optimierung $Tag")
if ($Draft) { $releaseArgs += "--draft" }

$notesPath = Join-Path $env:TEMP "rohre-release-notes-$Tag.txt"
[System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($false))
$releaseArgs += @("--notes-file", $notesPath)

& gh @releaseArgs
if ($LASTEXITCODE -ne 0) {
  throw "GitHub-Release fehlgeschlagen."
}

Remove-Item $notesPath -Force -ErrorAction SilentlyContinue

Write-Host "GitHub-Release erstellt: $Tag"
explorer.exe $releaseFolder
