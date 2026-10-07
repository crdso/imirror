@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-ble-hid-probe.ps1"
if errorlevel 1 pause
