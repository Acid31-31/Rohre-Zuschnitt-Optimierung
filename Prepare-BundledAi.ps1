param(
    [string]$Model = "moondream"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$vendorAi = Join-Path $root "vendor\AI"
$ollamaDir = Join-Path $vendorAi "ollama"
$modelsDir = Join-Path $vendorAi "models"
New-Item -ItemType Directory -Path $ollamaDir, $modelsDir -Force | Out-Null

$ollamaExe = Join-Path $ollamaDir "ollama.exe"
if (-not (Test-Path $ollamaExe)) {
    Write-Host "Lade Ollama Windows ZIP..."
    $api = Invoke-RestMethod -Uri "https://api.github.com/repos/ollama/ollama/releases/latest" -Headers @{ "User-Agent" = "RohreZuschnitt-PrepareAi" }
    $asset = $api.assets | Where-Object { $_.name -eq "ollama-windows-amd64.zip" } | Select-Object -First 1
    if (-not $asset) { throw "ollama-windows-amd64.zip nicht in Latest-Release gefunden." }
    $zipPath = Join-Path $env:TEMP "ollama-windows-amd64.zip"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $ollamaDir -Force
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $ollamaExe)) {
        $found = Get-ChildItem $ollamaDir -Recurse -Filter ollama.exe | Select-Object -First 1
        if ($found) {
            $binDir = Split-Path $found.FullName
            Get-ChildItem $binDir -File | ForEach-Object {
                Copy-Item $_.FullName -Destination (Join-Path $ollamaDir $_.Name) -Force
            }
        }
    }
    if (-not (Test-Path $ollamaExe)) { throw "ollama.exe nach Entpacken nicht gefunden." }
    Write-Host "Ollama bereit: $ollamaExe"
}
else {
    Write-Host "Ollama bereits vorhanden: $ollamaExe"
}

# CUDA-Ordner streichen (zu groß für USB/GitHub; CPU/Vulkan reicht)
Get-ChildItem (Join-Path $ollamaDir "lib\ollama") -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like "cuda*" } |
  ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

$env:OLLAMA_MODELS = $modelsDir
$env:OLLAMA_HOST = "127.0.0.1:11435"

# Serve starten falls nötig
$serve = Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WorkingDirectory $ollamaDir -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 3
try {
    Write-Host "Lade Vision-Modell '$Model' nach AI\models (einmalig, kann mehrere Minuten dauern)..."
    & $ollamaExe pull $Model
    if ($LASTEXITCODE -ne 0) { throw "ollama pull $Model fehlgeschlagen." }
    Write-Host "Modell geladen unter $modelsDir"
}
finally {
    if ($serve -and -not $serve.HasExited) {
        Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Fertig. vendor\AI ist bereit zum Kopieren in die USB-Version."
