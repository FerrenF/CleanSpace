@echo off
setlocal enabledelayedexpansion

rem Args
set "OUTDIR=%~1"
set "ZIPNAME=%~2"

if "%OUTDIR%"=="" (
    echo [ERROR] Missing OutDir argument
    exit /b 1
)
if "%ZIPNAME%"=="" (
    echo [ERROR] Missing ZipName argument
    exit /b 2
)

rem Ensure no trailing backslash
set "OUTDIR=%OUTDIR:~0,-1%"

rem Full path of output zip
set "ZIPPATH=%OUTDIR%\%ZIPNAME%"

rem Remove existing zip if it exists
if exist "%ZIPPATH%" del /f /q "%ZIPPATH%"

echo Creating zip: %ZIPPATH%
powershell -Command "Compress-Archive -Path '%OUTDIR%\*' -DestinationPath '%ZIPPATH%' -Force"

if errorlevel 1 (
    echo [ERROR] Failed to create archive.
    exit /b 3
)

echo [SUCCESS] Built %ZIPPATH%
endlocal
exit /b 0