@echo off
cd /d "%~dp0"
taskkill /IM WindowsTopMenuBar.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
if not exist "bin\Debug\net10.0-windows\WindowsTopMenuBar.exe" (
  echo Building Windows Top Menu Bar...
  dotnet build -c Debug --nologo -v q
  if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
  )
)
start "" "%~dp0bin\Debug\net10.0-windows\WindowsTopMenuBar.exe"
