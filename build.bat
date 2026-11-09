@echo off
rem Builds Cooldown.exe next to this file: one exe that needs the .NET 9 Desktop Runtime.
rem Quit Cooldown first (tray icon > Exit), or the old exe can't be replaced.
cd /d "%~dp0"
dotnet publish src\Cooldown -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .
if errorlevel 1 pause
