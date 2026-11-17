@echo off
rem SPERIMENTALE: sostituisce il trascinamento col tasto sinistro sulle cartelle. Rimovibile con uninstall_drop.bat.
cd /d "%~dp0"
regsvr32 /s /n /i:drop "%~dp0SmartMoveShell.dll" || (echo Registrazione fallita & exit /b 1)
echo Fatto. Se qualcosa non va con il trascinamento, esegui uninstall_drop.bat
