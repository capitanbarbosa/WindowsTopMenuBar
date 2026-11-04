@echo off
taskkill /IM WindowsWhisker.exe /F >nul 2>&1
if errorlevel 1 (
  echo Windows Whisker was not running.
) else (
  echo Stopped Windows Whisker.
)
timeout /t 1 /nobreak >nul
