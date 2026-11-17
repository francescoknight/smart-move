# Aggiorna Smart Move all'ultima versione pubblicata (stessa cartella di questo script).
$ErrorActionPreference = 'Stop'
$dir  = $PSScriptRoot
$base = 'https://github.com/francescoknight/smart-move/releases/download/latest'
$tmp  = Join-Path $env:TEMP ('smartmove_upd_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try {
    Write-Host '1/5 Scarico l''ultima versione...'
    Invoke-WebRequest "$base/SmartMove.zip" -OutFile "$tmp\SmartMove.zip" -UseBasicParsing
    Expand-Archive "$tmp\SmartMove.zip" "$tmp\new" -Force

    Write-Host '2/5 Chiudo Smart Move...'
    Get-Process SmartMove -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # La DLL è caricata da Esplora: si può rinominare ma non sovrascrivere.
    $oldDll = Join-Path $dir 'SmartMoveShell.dll'; $newDll = "$tmp\new\SmartMoveShell.dll"
    function Hash($f) { $h=[Security.Cryptography.SHA256]::Create(); $b=[IO.File]::ReadAllBytes($f); [BitConverter]::ToString($h.ComputeHash($b)) }
    $dllChanged = (-not (Test-Path $oldDll)) -or ((Hash $oldDll) -ne (Hash $newDll))

    Write-Host '3/5 Sostituisco i file...'
    if ($dllChanged -and (Test-Path $oldDll)) { Move-Item $oldDll ("$oldDll.old" + (Get-Random)) -Force }
    Get-ChildItem "$tmp\new" -File | Where-Object {
        $_.Name -notin 'update.bat','update.ps1' -and ($dllChanged -or $_.Name -ne 'SmartMoveShell.dll')
    } | ForEach-Object { Copy-Item $_.FullName (Join-Path $dir $_.Name) -Force }

    Write-Host '4/5 Registro i componenti...'
    & cmd /c "`"$dir\install.bat`" nostart" | Out-Null

    if ($dllChanged -and (Get-Process explorer -ErrorAction SilentlyContinue)) {
        Write-Host '    Riavvio Esplora file (lo schermo lampeggia per un attimo)...'
        Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    }
    Get-ChildItem $dir -Filter '*.old*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    Write-Host '5/5 Riavvio Smart Move...'
    if ($env:SMARTMOVE_CI -ne '1') { Start-Process (Join-Path $dir 'SmartMove.exe') -ArgumentList '--tray' }
    Write-Host 'Aggiornamento completato.'
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
