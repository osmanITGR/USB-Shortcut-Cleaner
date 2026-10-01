@echo off
title USB Shortcut Virus Cleaner
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cleaner_GUI.ps1"
if %errorlevel% neq 0 (
    echo.
    echo An error occurred running the cleaner.
    pause
)
