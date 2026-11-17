@echo off
rem Installazione per-utente (nessun admin). Eseguire dalla cartella con SmartMove.exe e SmartMoveShell.dll
cd /d "%~dp0"
regsvr32 /s "%~dp0SmartMoveShell.dll" || (echo Registrazione DLL fallita & exit /b 1)
reg add "HKCU\Software\Classes\Directory\Background\shell\SmartMovePaste" /ve /d "Incolla (Smart Move)" /f >nul
reg add "HKCU\Software\Classes\Directory\Background\shell\SmartMovePaste\command" /ve /d "\"%~dp0SmartMove.exe\" --paste \"%%V\"" /f >nul
rem Processo residente (Ctrl+V) all'avvio di Windows
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SmartMove /d "\"%~dp0SmartMove.exe\" --tray" /f >nul
if /i not "%~1"=="nostart" start "" "%~dp0SmartMove.exe" --tray
echo.
echo Installato:
echo   - Ctrl+V in Esplora file e sul Desktop (processo residente nell'area di notifica)
echo   - trascinamento col TASTO DESTRO: "Smart Move qui" / "Smart Copy qui"
echo   - tasto destro nello sfondo di una cartella: "Incolla (Smart Move)"
echo Per il trascinamento col TASTO SINISTRO (sperimentale) esegui anche install_drop.bat
