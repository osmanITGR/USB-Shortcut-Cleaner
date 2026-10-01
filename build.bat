@echo off
title Build USB Shortcut Cleaner
echo Compiling USB_Shortcut_Cleaner.exe using native csc.exe ...

if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
) else (
    set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll /out:USB_Shortcut_Cleaner.exe Program.cs

if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] USB_Shortcut_Cleaner.exe compiled successfully!
) else (
    echo.
    echo [ERROR] Compilation failed.
)
pause
