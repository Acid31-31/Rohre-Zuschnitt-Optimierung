@echo off
setlocal
cd /d "%~dp0"

set "ALLOW=%~dp0KEY_Rohre_Zuschitt.allow"
if not exist "%ALLOW%" type nul > "%ALLOW%"

set "OUT=%~dp0KEY_Rohre_Zuschitt"
if exist "%OUT%\RohreZuschnittOptimierung.exe" (
  copy /Y "%OUT%\RohreZuschnittOptimierung.exe" "%OUT%\KEY_Rohre_Zuschitt.exe" >nul
  copy /Y "%ALLOW%" "%OUT%\KEY_Rohre_Zuschitt.allow" >nul
  start "" /D "%OUT%" "%OUT%\KEY_Rohre_Zuschitt.exe"
  exit /b 0
)

echo KEY_Rohre_Zuschitt ist noch nicht gebaut.
echo Bitte warten oder create-usb-version.ps1 ausfuehren.
pause
exit /b 1
