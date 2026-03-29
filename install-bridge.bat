@echo off
title WellVersed Bridge Installer
echo.
echo  ========================================
echo   WellVersed Bridge Installer
echo  ========================================
echo.

if "%~1"=="" (
    echo  Usage: install-bridge.bat "C:\path\to\your\UEFN\project"
    echo.
    echo  The project folder should contain a .uefnproject file.
    echo.
    set /p UEFN_PROJECT="  Enter your UEFN project path: "
) else (
    set UEFN_PROJECT=%~1
)

:: Validate project path
if not exist "%UEFN_PROJECT%\*.uefnproject" (
    echo.
    echo  [ERROR] No .uefnproject file found in: %UEFN_PROJECT%
    echo  Make sure you point to the root of your UEFN project.
    echo.
    pause
    exit /b 1
)

:: Create Content/Python if it doesn't exist
if not exist "%UEFN_PROJECT%\Content\Python" (
    mkdir "%UEFN_PROJECT%\Content\Python"
    echo  [OK] Created Content\Python directory
)

:: Copy wellversed bridge package
echo  [..] Copying WellVersed bridge...
xcopy /E /I /Y "%~dp0bridge\wellversed" "%UEFN_PROJECT%\Content\Python\wellversed" >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] Failed to copy bridge files
    pause
    exit /b 1
)
echo  [OK] Copied wellversed/ package

:: Copy or merge init_unreal.py
if exist "%UEFN_PROJECT%\Content\Python\init_unreal.py" (
    :: Check if already has wellversed import
    findstr /C:"wellversed" "%UEFN_PROJECT%\Content\Python\init_unreal.py" >nul 2>&1
    if errorlevel 1 (
        echo.>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo # WellVersed Bridge auto-start>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo try:>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo     import wellversed>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo     wellversed.start()>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo     print("[WellVersed] Bridge started")>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo except Exception as e:>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo     print(f"[WellVersed] Failed: {e}")>>"%UEFN_PROJECT%\Content\Python\init_unreal.py"
        echo  [OK] Appended WellVersed to existing init_unreal.py
    ) else (
        echo  [OK] init_unreal.py already has WellVersed — skipped
    )
) else (
    copy /Y "%~dp0bridge\init_unreal.py" "%UEFN_PROJECT%\Content\Python\init_unreal.py" >nul
    echo  [OK] Copied init_unreal.py
)

:: Remove __pycache__ (don't need compiled bytecode from our dev machine)
if exist "%UEFN_PROJECT%\Content\Python\wellversed\__pycache__" (
    rmdir /S /Q "%UEFN_PROJECT%\Content\Python\wellversed\__pycache__" 2>nul
)
if exist "%UEFN_PROJECT%\Content\Python\wellversed\commands\__pycache__" (
    rmdir /S /Q "%UEFN_PROJECT%\Content\Python\wellversed\commands\__pycache__" 2>nul
)

echo.
echo  ========================================
echo   Installation Complete!
echo  ========================================
echo.
echo  Bridge installed to: %UEFN_PROJECT%\Content\Python\wellversed\
echo.
echo  Next steps:
echo    1. Enable "Python Editor Scripting" in UEFN Project Settings
echo    2. Restart UEFN
echo    3. Launch WellVersed (run wellversed.bat)
echo    4. Green connection indicator = bridge active
echo.
pause
