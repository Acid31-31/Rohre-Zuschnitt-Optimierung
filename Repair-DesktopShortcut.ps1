param(
    [string]$TargetExe = 'Z:\Rohre-Zuschnitt\Rohre-Zuschnitt-R31\RohreZuschnittOptimierung.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $TargetExe)) {
    throw "Programm nicht gefunden: $TargetExe"
}

$baseDir = Split-Path -Parent $TargetExe
$iconPath = Join-Path $baseDir 'AppIcon.ico'
if (-not (Test-Path $iconPath)) {
    $iconPath = $TargetExe
}

$wsh = New-Object -ComObject WScript.Shell
$desktopPaths = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('CommonDesktopDirectory')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

$shortcutName = 'Rohre Zuschnitt Optimierung.lnk'
foreach ($desktop in $desktopPaths) {
    $shortcutPath = Join-Path $desktop $shortcutName
    try {
        $shortcut = $wsh.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $TargetExe
        $shortcut.WorkingDirectory = $baseDir
        $shortcut.IconLocation = if ($iconPath.EndsWith('.ico')) { $iconPath } else { "$TargetExe,0" }
        $shortcut.Description = 'Rohre Zuschnitt Optimierung starten'
        $shortcut.Save()
        Write-Host "Verknuepfung repariert: $shortcutPath"
    }
    catch {
        Write-Warning "Verknuepfung konnte nicht gespeichert werden: $shortcutPath ($($_.Exception.Message))"
    }
}

Write-Host "Ziel: $TargetExe"
