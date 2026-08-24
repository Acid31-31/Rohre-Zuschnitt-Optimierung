param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [string]$CertPath = "",
    [string]$CertPasswordFile = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Resolve-CodeSigningCert.ps1")
$certInfo = Resolve-CodeSigningCert -Root $root

if (-not (Test-Path $PackageRoot)) {
    throw "Paketordner nicht gefunden: $PackageRoot"
}

if ([string]::IsNullOrWhiteSpace($CertPath)) {
    $CertPath = $certInfo.PfxPath
}
if ([string]::IsNullOrWhiteSpace($CertPasswordFile)) {
    $CertPasswordFile = $certInfo.PasswordPath
}

if ([string]::IsNullOrWhiteSpace($CertPath) -or -not (Test-Path $CertPath)) {
    Write-Warning "Zertifikat fehlt - Paket-EXEs werden nicht signiert."
    exit 0
}

$CertPassword = Get-CodeSigningPassword -PasswordPath $CertPasswordFile -CertPassword $CertPassword
$signtool = Get-SignToolPath

$executables = Get-ChildItem $PackageRoot -Filter *.exe -Recurse -File | Sort-Object FullName
if ($executables.Count -eq 0) {
    Write-Host "Keine EXE-Dateien in $PackageRoot"
    exit 0
}

Write-Host "Signiere $($executables.Count) EXE(s) in $PackageRoot ..."
foreach ($exe in $executables) {
    Write-Host "  -> $($exe.Name)"
    $signed = $false
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $exe.FullName
        if ($LASTEXITCODE -eq 0) {
            $signed = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $signed) {
        throw "Signieren fehlgeschlagen: $($exe.FullName)"
    }
}

Write-Host "Alle Paket-EXEs signiert."
