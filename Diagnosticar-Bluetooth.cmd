@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\open-bluetooth-link-trace.ps1" -StartIMirror
if errorlevel 1 pause
