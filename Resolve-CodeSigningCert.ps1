function Resolve-CodeSigningCert {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $siblingDokRoot = Join-Path (Split-Path $Root -Parent) "PDF Sortieren"

    $pfxCandidates = @(
        (Join-Path $Root "cert\DOK-CodeSigning.pfx"),
        (Join-Path $siblingDokRoot "cert\DOK-CodeSigning.pfx")
    )

    $passwordCandidates = @(
        (Join-Path $Root "cert\signing-password.txt"),
        (Join-Path $siblingDokRoot "cert\signing-password.txt")
    )

    $publicCerCandidates = @(
        (Join-Path $Root "CodeSigning.cer"),
        (Join-Path $Root "cert\DOK-CodeSigning.cer"),
        (Join-Path $siblingDokRoot "CodeSigning.cer"),
        (Join-Path $siblingDokRoot "cert\DOK-CodeSigning.cer")
    )

    $pfxPath = $pfxCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    $passwordPath = $passwordCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    $publicCerPath = $publicCerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    return [pscustomobject]@{
        PfxPath         = $pfxPath
        PasswordPath    = $passwordPath
        PublicCerPath   = $publicCerPath
        IsAvailable     = -not [string]::IsNullOrWhiteSpace($pfxPath)
    }
}

function Get-SignToolPath {
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not [string]::IsNullOrWhiteSpace($signtool)) {
        return $signtool
    }

    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if ([string]::IsNullOrWhiteSpace($signtool)) {
        throw "signtool.exe nicht gefunden. Windows SDK / Visual Studio Build Tools installieren."
    }

    return $signtool
}

function Get-CodeSigningPassword {
    param(
        [string]$PasswordPath,
        [string]$CertPassword = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {
        return $CertPassword
    }

    if (-not [string]::IsNullOrWhiteSpace($PasswordPath) -and (Test-Path $PasswordPath)) {
        return (Get-Content $PasswordPath -Raw).Trim()
    }

    throw "Kein Zertifikat-Passwort. Parameter -CertPassword oder signing-password.txt angeben."
}

function Export-CodeSigningCer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PfxPath,
        [Parameter(Mandatory = $true)]
        [string]$CertPassword,
        [Parameter(Mandatory = $true)]
        [string[]]$TargetPaths
    )

    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $CertPassword)
    $bytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)

    foreach ($target in $TargetPaths) {
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }

        $directory = Split-Path $target -Parent
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        [System.IO.File]::WriteAllBytes($target, $bytes)
    }
}
