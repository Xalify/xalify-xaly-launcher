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
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
set WV=
if not exist "webview2\Microsoft.Web.WebView2.WinForms.dll" (
  echo Getting the WebView2 display engine files (one time only^)...
  powershell -NoProfile -ExecutionPolicy Bypass -File "tools\get-webview2.ps1"
)
if exist "webview2\Microsoft.Web.WebView2.WinForms.dll" (
  set WV=/define:WEBVIEW2 /r:webview2\Microsoft.Web.WebView2.Core.dll /r:webview2\Microsoft.Web.WebView2.WinForms.dll
  copy /y "webview2\*.dll" . >nul
  for %%a in (x64 x86 arm64) do if exist "webview2\%%a" xcopy /y /q /i "webview2\%%a" "%%a" >nul
) else (
  echo WebView2 files could not be downloaded, building with the old Internet Explorer engine only.
)
"%CSC%" /nologo /target:winexe /optimize+ /out:"Xaly Launcher.exe" /win32icon:xaly.ico %REFS% %WV% Xaly.cs
if errorlevel 1 (
  echo.
  echo The build failed. Please send the messages above to Claude.
  pause
  exit /b 1
)
echo.
echo Done. Starting Xaly Launcher...
start "" "Xaly Launcher.exe"
