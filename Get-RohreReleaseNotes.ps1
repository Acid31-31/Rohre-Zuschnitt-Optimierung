function Get-RohreReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RevisionLabel
    )

    $changelogPath = Join-Path $Root "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        throw "CHANGELOG.md nicht gefunden: $changelogPath"
    }

    $content = Get-Content $changelogPath -Raw
    $escapedRevision = [regex]::Escape($RevisionLabel.Trim())
    $sectionPattern = '(?ms)^##\s+' + $escapedRevision + '\s*(?:\r?\n)(?<body>.*?)(?=^\s*##\s+|\z)'
    $match = [regex]::Match($content, $sectionPattern)

    if (-not $match.Success) {
        throw "Kein Changelog-Abschnitt fuer $RevisionLabel in CHANGELOG.md gefunden."
    }

    $items = @()
    foreach ($line in ($match.Groups["body"].Value -split "\r?\n")) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("- ")) {
            $items += $trimmed.Substring(2).Trim()
        }
    }

    if ($items.Count -eq 0) {
        throw "Changelog-Abschnitt $RevisionLabel enthaelt keine Eintraege (- ...)."
    }

    return $items
}
