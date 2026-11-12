@echo off
rem Builds RamAltman.exe with the C# compiler that ships with Windows. No installs needed.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:"%~dp0app.manifest" /out:"%~dp0RamAltman.exe" "%~dp0RamAltman.cs"
if errorlevel 1 (echo Build failed. & exit /b 1)
echo Built "%~dp0RamAltman.exe"
