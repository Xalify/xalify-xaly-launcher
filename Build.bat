@echo off
cd /d "%~dp0"
echo Building Xaly Launcher...
taskkill /f /im "Xaly Launcher.exe" >nul 2>&1
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the C# compiler that comes with Windows.
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /out:"Xaly Launcher.exe" /win32icon:xaly.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll Xaly.cs
if errorlevel 1 (
  echo.
  echo The build failed. Please send the messages above to Claude.
  pause
  exit /b 1
)
echo.
echo Done. Starting Xaly Launcher...
start "" "Xaly Launcher.exe"
