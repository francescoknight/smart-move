@echo off
rem Richiede .NET 8 SDK e (per la DLL) MinGW-w64 oppure Visual Studio Build Tools.
dotnet publish src\SmartMove.App -c Release -r win-x64 --self-contained false -o dist || exit /b 1
where g++ >nul 2>nul && (g++ -O2 -std=c++17 -shared -static -static-libgcc -static-libstdc++ -o dist\SmartMoveShell.dll shell\SmartMoveShell.cpp shell\SmartMoveShell.def -lole32 -lshell32 -lshlwapi -luuid -Wl,--kill-at) || (
  cl /nologo /O2 /EHsc /LD shell\SmartMoveShell.cpp /Fe:dist\SmartMoveShell.dll /link /DEF:shell\SmartMoveShell.def ole32.lib shell32.lib shlwapi.lib uuid.lib)
copy /y *.bat dist\ >nul & del dist\build.bat
echo Fatto: cartella dist\
