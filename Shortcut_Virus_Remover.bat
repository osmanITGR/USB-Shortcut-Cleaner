@echo off
setlocal EnableDelayedExpansion
title USB Shortcut Virus Remover & File Restorer
color 0A

:MENU
cls
echo ======================================================================
echo             USB Shortcut Virus Cleaner ^& File Restorer
echo ======================================================================
echo.
echo Connected Drives:
powershell -NoProfile -Command "Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 2 -or $_.DeviceID -notin @('C:','D:') } | ForEach-Object { Write-Host ('  ' + $_.DeviceID + ' [' + $_.VolumeName + ']  Free: ' + [math]::Round($_.FreeSpace/1GB,1) + 'GB / ' + [math]::Round($_.Size/1GB,1) + 'GB') }"
echo.
echo ----------------------------------------------------------------------
echo Enter the Drive Letter you want to clean (e.g., F or E)
echo Or type Q to Quit.
echo ----------------------------------------------------------------------
echo.

set /p DRIVE_LETTER="Enter Drive Letter: "

if /i "%DRIVE_LETTER%"=="Q" goto EXIT
if "%DRIVE_LETTER%"=="" goto MENU

:: Strip colon or extra characters
set DRIVE_LETTER=%DRIVE_LETTER:~0,1%
set TARGET_DRIVE=%DRIVE_LETTER%:

if not exist "%TARGET_DRIVE%\" (
    color 0C
    echo.
    echo [ERROR] Drive %TARGET_DRIVE% was not found! Please check and try again.
    pause
    color 0A
    goto MENU
)

echo.
echo ======================================================================
echo Cleaning and restoring Drive %TARGET_DRIVE% ...
echo ======================================================================
echo.

:: 1. Terminate running script engines
echo [1/5] Stopping any rogue background scripts (wscript/cscript)...
taskkill /f /im wscript.exe >nul 2>&1
taskkill /f /im cscript.exe >nul 2>&1

:: 2. Delete fake shortcuts
echo [2/5] Removing fake shortcut files (*.lnk)...
del /f /q /a "%TARGET_DRIVE%\*.lnk" >nul 2>&1

:: 3. Delete malware directories and scripts
echo [3/5] Removing malware folders and script files...
if exist "%TARGET_DRIVE%\sysvolume" (
    attrib -h -r -s "%TARGET_DRIVE%\sysvolume" >nul 2>&1
    rd /s /q "%TARGET_DRIVE%\sysvolume" >nul 2>&1
)
if exist "%TARGET_DRIVE%\drive" (
    attrib -h -r -s "%TARGET_DRIVE%\drive" >nul 2>&1
    rd /s /q "%TARGET_DRIVE%\drive" >nul 2>&1
)
if exist "%TARGET_DRIVE%\WindowsServices" (
    attrib -h -r -s "%TARGET_DRIVE%\WindowsServices" >nul 2>&1
    rd /s /q "%TARGET_DRIVE%\WindowsServices" >nul 2>&1
)
if exist "%TARGET_DRIVE%\autorun.inf" (
    attrib -h -r -s "%TARGET_DRIVE%\autorun.inf" >nul 2>&1
    del /f /q "%TARGET_DRIVE%\autorun.inf" >nul 2>&1
)
del /f /q /a "%TARGET_DRIVE%\*.vbs" >nul 2>&1
del /f /q /a "%TARGET_DRIVE%\*.js" >nul 2>&1

:: 4. Unhide all files and folders
echo [4/5] Unhiding files and folders (running attrib -h -r -s)...
echo       Please wait a moment...
attrib -h -r -s /s /d "%TARGET_DRIVE%\*.*" >nul 2>&1

:: 5. Restore files trapped inside a subfolder matching the volume name
echo [5/5] Checking for trapped files in folder...
powershell -NoProfile -Command "$drv = '%TARGET_DRIVE%\'; $vol = (Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq '%TARGET_DRIVE%' }).VolumeName; if ($vol -and (Test-Path (Join-Path $drv $vol))) { $sub = Join-Path $drv $vol; attrib.exe -h -r -s $sub; Get-ChildItem -LiteralPath $sub | Move-Item -Destination $drv -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $sub -Force -ErrorAction SilentlyContinue }" >nul 2>&1

echo.
echo ======================================================================
echo  [SUCCESS] Drive %TARGET_DRIVE% is clean and files are restored!
echo ======================================================================
echo.
start "" explorer.exe "%TARGET_DRIVE%\"
pause
goto MENU

:EXIT
exit
