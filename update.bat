@echo off
rem Metti questo file nella cartella di SmartMove.exe e fai doppio clic: scarica e installa l'ultima versione.
cd /d "%~dp0"
echo Scarico l'aggiornatore...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; Invoke-WebRequest 'https://github.com/francescoknight/smart-move/releases/download/latest/update.ps1' -OutFile '%~dp0update.ps1' -UseBasicParsing"
if errorlevel 1 (echo Impossibile scaricare l'aggiornatore. Controlla la connessione. & if not "%SMARTMOVE_CI%"=="1" pause & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1"
if errorlevel 1 (echo Aggiornamento FALLITO. & if not "%SMARTMOVE_CI%"=="1" pause & exit /b 1)
if not "%SMARTMOVE_CI%"=="1" timeout /t 3 >nul
