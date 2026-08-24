function Get-RohreRevisionFromProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $csprojPath = Join-Path $Root "RohreZuschnittOptimierung.csproj"
    if (-not (Test-Path $csprojPath)) {
        throw "Projektdatei nicht gefunden: $csprojPath"
    }

    $xml = [xml](Get-Content $csprojPath)
    $assemblyVersion = $null
    foreach ($group in $xml.Project.PropertyGroup) {
        if ($group.AssemblyVersion) {
            $assemblyVersion = "$($group.AssemblyVersion)"
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($assemblyVersion)) {
        throw "AssemblyVersion nicht gefunden in RohreZuschnittOptimierung.csproj"
    }

    $parts = $assemblyVersion.Split('.')
    $revision = 0
    if ($parts.Length -ge 4) {
        [void][int]::TryParse($parts[3], [ref]$revision)
    }
    if ($revision -le 0 -and $parts.Length -ge 3) {
        [void][int]::TryParse($parts[2], [ref]$revision)
    }

    $revLabel = "R$revision"
    return [pscustomobject]@{
        Revision       = $revision
        RevisionLabel  = $revLabel
        VersionTag     = "v$($parts[0]).$($parts[1])-$revLabel"
        ProductFolder  = "Rohre-Zuschnitt-$revLabel"
        UsbZipName     = "Rohre-Zuschnitt-$revLabel.zip"
        ReleaseZipName = "RohreZuschnittOptimierung-Release-$revLabel.zip"
    }
}
