@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-And-Build.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Copy the complete error output.
  pause
  exit /b 1
)
echo.
echo Build completed.
pause
endlocal
