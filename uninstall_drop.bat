@echo off
cd /d "%~dp0"
regsvr32 /s /u /n /i:drop "%~dp0SmartMoveShell.dll"
reg delete "HKCU\Software\Classes\Directory\shellex\DropHandler" /f >nul 2>nul
reg delete "HKCU\Software\Classes\Drive\shellex\DropHandler" /f >nul 2>nul
echo Drop handler rimosso.
