function Get-RohreChangelogSections {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $changelogPath = Join-Path $Root "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        throw "CHANGELOG.md nicht gefunden: $changelogPath"
    }

    $content = Get-Content $changelogPath -Raw
    $matches = [regex]::Matches(
        $content,
        '(?ms)^##\s+(?<label>R(?<rev>\d+))\s*(?:\r?\n)(?<body>.*?)(?=^\s*##\s+|\z)'
    )

    $sections = @()
    foreach ($match in $matches) {
        $items = @()
        foreach ($line in ($match.Groups["body"].Value -split "\r?\n")) {
            $trimmed = $line.Trim()
            if ($trimmed.StartsWith("- ")) {
                $items += $trimmed.Substring(2).Trim()
            }
        }

        if ($items.Count -eq 0) {
            continue
        }

        $sections += [pscustomobject]@{
            Label    = $match.Groups["label"].Value
            Revision = [int]$match.Groups["rev"].Value
            Items    = $items
        }
    }

    return $sections
}

function Get-RohreReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RevisionLabel,

        [switch]$IncludeOlder,

        [int]$MaxOlderRevisions = 3
    )

    $sections = @(Get-RohreChangelogSections -Root $Root)
    $current = $sections | Where-Object { $_.Label -eq $RevisionLabel } | Select-Object -First 1
    if (-not $current) {
        throw "Kein Changelog-Abschnitt fuer $RevisionLabel in CHANGELOG.md gefunden."
    }

    $items = @($current.Items)
    if ($IncludeOlder) {
        $olderCount = 0
        foreach ($section in $sections) {
            if ($section.Revision -ge $current.Revision) {
                continue
            }

            foreach ($item in $section.Items) {
                $items += "$($section.Label): $item"
            }

            $olderCount++
            if ($olderCount -ge $MaxOlderRevisions) {
                break
            }
        }
    }

    if ($items.Count -eq 0) {
        throw "Changelog-Abschnitt $RevisionLabel enthaelt keine Eintraege (- ...)."
    }

    return $items
}
