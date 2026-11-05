@echo off
taskkill /IM WindowsTopMenuBar.exe /F >nul 2>&1
if errorlevel 1 (
  echo Windows Top Menu Bar was not running.
) else (
  echo Stopped Windows Top Menu Bar.
)
timeout /t 1 /nobreak >nul
