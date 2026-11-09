@echo off
rem Compiles ResetTimer.exe with the C# compiler built into Windows (.NET Framework 4).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:ResetTimer.exe ResetTimer.cs Island.cs
if errorlevel 1 pause
