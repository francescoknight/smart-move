@echo off
cd /d "%~dp0"
call "%~dp0uninstall_drop.bat" >nul 2>nul
taskkill /f /im SmartMove.exe >nul 2>nul
regsvr32 /u /s "%~dp0SmartMoveShell.dll"
reg delete "HKCU\Software\Classes\Directory\Background\shell\SmartMovePaste" /f >nul 2>nul
reg delete "HKCU\Software\Classes\Directory\shell\SmartMoveDupes" /f >nul 2>nul
reg delete "HKCU\Software\Classes\Directory\Background\shell\SmartMoveDupes" /f >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SmartMove /f >nul 2>nul
echo Rimosso. (Se Esplora tiene ancora la DLL caricata, riavvia Esplora risorse o esci/rientra da Windows per poterla cancellare.)
