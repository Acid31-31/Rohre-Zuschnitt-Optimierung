#Export-HtmlToPdf.ps1 — HTML → PDF via Edge/Chrome headless
param(
    [Parameter(Mandatory = $true)]
    [string]$HtmlPath,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
$HtmlPath = (Resolve-Path -LiteralPath $HtmlPath).Path
$PdfPath = [System.IO.Path]::ChangeExtension($HtmlPath, '.pdf')

$browser = @(
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $browser) {
    throw 'Kein Edge oder Chrome gefunden.'
}

if (Test-Path -LiteralPath $PdfPath) {
    Remove-Item -LiteralPath $PdfPath -Force
}

& $browser --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$PdfPath" $HtmlPath 2>$null

$deadline = (Get-Date).AddSeconds(15)
while (-not (Test-Path -LiteralPath $PdfPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path -LiteralPath $PdfPath)) {
    throw "PDF wurde nicht erzeugt: $PdfPath"
}

Write-Host "PDF erzeugt: $PdfPath"

if ($Open) {
    Start-Process -FilePath $PdfPath
}
