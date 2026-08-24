param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$CertPath = "",
    [string]$CertPasswordFile = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Resolve-CodeSigningCert.ps1")
$certInfo = Resolve-CodeSigningCert -Root $root

if ([string]::IsNullOrWhiteSpace($CertPath)) {
    $CertPath = $certInfo.PfxPath
}
if ([string]::IsNullOrWhiteSpace($CertPasswordFile)) {
    $CertPasswordFile = $certInfo.PasswordPath
}

$exePath = Get-ChildItem (Join-Path $root "bin\$Configuration") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "net8.0-windows*" } |
    ForEach-Object { Join-Path $_.FullName "RohreZuschnittOptimierung.exe" } |
    Where-Object { Test-Path $_ } |
    Sort-Object { (Get-Item $_).LastWriteTime } -Descending |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($exePath) -or -not (Test-Path $exePath)) {
    throw "EXE nicht gefunden unter bin\$Configuration\net8.0-windows*`nBitte zuerst bauen."
}
Write-Host "Signiere: $exePath"

if ([string]::IsNullOrWhiteSpace($CertPath) -or -not (Test-Path $CertPath)) {
    throw "Zertifikat nicht gefunden. Erwartet: cert\DOK-CodeSigning.pfx oder DOK-V01 cert-Ordner."
}

$CertPassword = Get-CodeSigningPassword -PasswordPath $CertPasswordFile -CertPassword $CertPassword
$signtool = Get-SignToolPath

Write-Host "Signiere: $exePath"
& $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $exePath
if ($LASTEXITCODE -ne 0) {
    throw "Signieren fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

Write-Host "Signatur pruefen..."
& $signtool verify /pa /v $exePath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Signatur vorhanden, Zertifikat ist auf diesem PC ggf. noch nicht vertrauenswuerdig."
}
else {
    Write-Host "Signatur verifiziert."
}

$releaseCer = Join-Path (Split-Path $exePath -Parent) "CodeSigning.cer"
$rootCer = Join-Path $root "CodeSigning.cer"
Export-CodeSigningCer -PfxPath $CertPath -CertPassword $CertPassword -TargetPaths @($releaseCer, $rootCer)
Write-Host "CodeSigning.cer exportiert: $rootCer"

Write-Host "Erfolgreich signiert ($Configuration)."
