@echo off
setlocal
cd /d "%~dp0"

set "ALLOW=%~dp0KEY_Rohre_Zuschitt.allow"
if not exist "%ALLOW%" type nul > "%ALLOW%"

set "ABS=Z:\Programierung\Rohre-Zuschnitt-Absicherung\KEY_Rohre_Zuschitt"
if exist "%ABS%\KEY_Rohre_Zuschitt.exe" if exist "%ABS%\hostfxr.dll" (
  copy /Y "%ALLOW%" "%ABS%\KEY_Rohre_Zuschitt.allow" >nul
  start "" /D "%ABS%" "%ABS%\KEY_Rohre_Zuschitt.exe"
  exit /b 0
)

set "PUB=%~dp0publish\win-x64-sc"
if exist "%PUB%\KEY_Rohre_Zuschitt.exe" if exist "%PUB%\hostfxr.dll" (
  copy /Y "%ALLOW%" "%PUB%\KEY_Rohre_Zuschitt.allow" >nul
  start "" /D "%PUB%" "%PUB%\KEY_Rohre_Zuschitt.exe"
  exit /b 0
)

set "OUT=%~dp0KEY_Rohre_Zuschitt"
if exist "%OUT%\RohreZuschnittOptimierung.exe" (
  copy /Y "%OUT%\RohreZuschnittOptimierung.exe" "%OUT%\KEY_Rohre_Zuschitt.exe" >nul
  copy /Y "%ALLOW%" "%OUT%\KEY_Rohre_Zuschitt.allow" >nul
  start "" /D "%OUT%" "%OUT%\KEY_Rohre_Zuschitt.exe"
  exit /b 0
)

echo KEY_Rohre_Zuschitt ist noch nicht gebaut.
echo Bitte create-usb-version.ps1 ausfuehren, danach diese Datei erneut starten.
pause
exit /b 1
