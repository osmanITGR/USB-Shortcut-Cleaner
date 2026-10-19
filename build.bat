@echo off
rem Builds USB_Shortcut_Cleaner.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:src\app.manifest ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /out:USB_Shortcut_Cleaner.exe src\UsbShortcutCleaner.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)
echo Build OK: USB_Shortcut_Cleaner.exe
