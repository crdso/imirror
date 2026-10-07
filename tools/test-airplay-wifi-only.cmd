@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0test-airplay-wifi-only.ps1" -Elevate
if errorlevel 1 pause
