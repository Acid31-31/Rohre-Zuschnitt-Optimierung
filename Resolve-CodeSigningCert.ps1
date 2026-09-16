function Resolve-CodeSigningCert {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $siblingDokRoot = Join-Path (Split-Path $Root -Parent) "DOK-V01-Optimierung"

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

    $pf86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if ([string]::IsNullOrWhiteSpace($pf86)) {
        $pf86 = "C:\Program Files (x86)"
    }

    $searchRoots = @(
        (Join-Path $pf86 "Windows Kits\10\bin"),
        (Join-Path ${env:ProgramFiles} "Windows Kits\10\bin"),
        "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
    )
    foreach ($kitRoot in $searchRoots) {
        if (-not (Test-Path $kitRoot)) {
            continue
        }

        if ((Split-Path $kitRoot -Leaf) -eq "x64" -and (Test-Path (Join-Path $kitRoot "signtool.exe"))) {
            return (Join-Path $kitRoot "signtool.exe")
        }

        $found = Get-ChildItem $kitRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq "x64" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not [string]::IsNullOrWhiteSpace($found)) {
            return $found
        }
    }

    throw "signtool.exe nicht gefunden. Windows SDK / Visual Studio Build Tools installieren."
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
