@echo off
REM ============================================================
REM  IntraBox clean rebuild script
REM  Fully removes build caches (obj/bin), runtime data
REM  (%%LOCALAPPDATA%%\IntraBox\Data and custom datapath.txt),
REM  and the old dist package, then rebuilds and redeploys.
REM  Next launch is first-run defaults (welcome, sample todo,
REM  empty history). Dist leftover json is not kept.
REM  Every exit path pauses AND writes build.log, so a
REM  double-clicked window never vanishes without a trace.
REM  NOTE: keep this file ASCII-only (English); chcp 65001 makes
REM  tool output render safely in the console.
REM ============================================================
setlocal
cd /d "%~dp0"
echo IntraBox rebuild starting...
chcp 65001 >nul
set "LOG=%~dp0build.log"
echo ==== IntraBox REBUILD %DATE% %TIME% ==== > "%LOG%"

set "MSBUILD="
if exist "%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe"

if not defined MSBUILD (
    echo [ERROR] MSBuild not found.
    echo [ERROR] MSBuild not found.>> "%LOG%"
    goto :fail
)

echo Using MSBuild: %MSBUILD%
echo Using MSBuild: %MSBUILD%>> "%LOG%"
echo.

REM IntraBox stays in the system tray: closing the window does NOT exit the process.
tasklist /FI "IMAGENAME eq IntraBox.exe" | "%SystemRoot%\System32\find.exe" /I "IntraBox.exe" >nul
set "RUNNING=%ERRORLEVEL%"
if "%RUNNING%"=="0" (
    echo ============================================================
    echo [ERROR] IntraBox is STILL RUNNING - check the system tray.
    echo         Right-click the tray icon and choose Exit first,
    echo         then run this script again.
    echo ============================================================
    echo [ERROR] IntraBox still running, aborted.>> "%LOG%"
    goto :fail
)

REM ---- 1. delete build caches (obj/bin) ----
echo [1/6] Deleting build caches - obj, bin...
if exist "src\IntraBox\obj" rmdir /S /Q "src\IntraBox\obj"
if exist "src\IntraBox\bin" rmdir /S /Q "src\IntraBox\bin"
if exist "tests\IntraBox.Tests\obj" rmdir /S /Q "tests\IntraBox.Tests\obj"
if exist "tests\IntraBox.Tests\bin" rmdir /S /Q "tests\IntraBox.Tests\bin"

REM ---- 2. reset runtime data to first-run defaults ----
REM config/history/todos live under %%LOCALAPPDATA%%\IntraBox\Data (or datapath.txt).
echo [2/6] Resetting user data to first-run defaults...
if exist "dist\IntraBox\datapath.txt" (
    for /f "usebackq delims=" %%D in ("dist\IntraBox\datapath.txt") do (
        if exist "%%D" rmdir /S /Q "%%D" 2>nul
    )
)
if exist "%LOCALAPPDATA%\IntraBox\Data" (
    rmdir /S /Q "%LOCALAPPDATA%\IntraBox\Data" 2>nul
    if exist "%LOCALAPPDATA%\IntraBox\Data" ( ping -n 3 127.0.0.1 >nul & rmdir /S /Q "%LOCALAPPDATA%\IntraBox\Data" 2>nul )
)
if exist "%LOCALAPPDATA%\IntraBox\Data" (
    echo [ERROR] Cannot delete %%LOCALAPPDATA%%\IntraBox\Data>> "%LOG%"
    echo ============================================================
    echo  [ERROR] Cannot delete user data folder.
    echo  Close IntraBox from the tray, then retry.
    echo ============================================================
    goto :fail
)

REM ---- 3. delete old dist package ----
echo [3/6] Deleting old dist package...
if exist "dist\IntraBox" (
    rmdir /S /Q "dist\IntraBox" 2>nul
    if exist "dist\IntraBox" ( ping -n 3 127.0.0.1 >nul & rmdir /S /Q "dist\IntraBox" 2>nul )
    if exist "dist\IntraBox" ( ping -n 4 127.0.0.1 >nul & rmdir /S /Q "dist\IntraBox" 2>nul )
)
if exist "dist\IntraBox" (
    echo [ERROR] dist\IntraBox is locked, listing locking processes...>> "%LOG%"
    REM Chinese messages are printed by the PS1 (UTF-8 safe); keep this .bat ASCII-only.
    powershell -NoProfile -ExecutionPolicy Bypass -File "tools\find-locking-processes.ps1" -Path "%CD%\dist\IntraBox"
    echo ============================================================
    echo  [ERROR] Cannot delete dist\IntraBox - directory in use.
    echo  See the process list above, close them, then retry.
    echo ============================================================
    goto :fail
)

REM ---- 4. clean build ----
echo [4/6] Building... NuGet restore then compile. Output is live below (also in build.log).
echo --- MSBuild clean --- >> "%LOG%"
REM Use explicit Rebuild (not Restore;Build): after wiping obj, Restore alone does
REM not re-register Tesseract's native x86/x64 assets, so a plain Build skips them.
"%MSBUILD%" "src\IntraBox\IntraBox.csproj" /t:"Restore;Rebuild" /p:Configuration=Release /p:NuGetAudit=false /m /v:m /fl "/flp:LogFile=%LOG%;Append;Encoding=UTF-8;Verbosity=minimal"
set "BUILDRC=%ERRORLEVEL%"
if not "%BUILDRC%"=="0" (
    echo [FAILED] Build failed.>> "%LOG%"
    goto :buildfail
)

REM ---- 5. redeploy to a fresh dist\IntraBox\ ----
echo [5/6] Deploying to dist\IntraBox\...
if not exist "dist\IntraBox" mkdir "dist\IntraBox"
REM Copy docs first so the mirror below does not treat them as extras to delete.
copy /Y "docs\*.txt" "dist\IntraBox\" >> "%LOG%" 2>&1
REM Exclude pdb + personal data files + docs txt (already copied above).
robocopy "src\IntraBox\bin\Release" "dist\IntraBox" /MIR /NFL /NDL /NJH /NP /R:2 /W:1 /XF *.pdb *.txt config.json history.json fileorganize.json fileorganize-undo.json /XD .claude >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
    echo [ERROR] Deploy to dist\IntraBox failed - robocopy code %RC%. See build.log.
    echo [ERROR] robocopy failed, code %RC%.>> "%LOG%"
    goto :fail
)

REM ---- 6. verify required runtime folders are present ----
echo [6/6] Verifying package completeness...
set "MISSING="
if not exist "dist\IntraBox\IntraBox.exe" set "MISSING=%MISSING% IntraBox.exe"
if not exist "dist\IntraBox\tessdata" set "MISSING=%MISSING% tessdata"
if not exist "dist\IntraBox\x86" set "MISSING=%MISSING% x86"
if not exist "dist\IntraBox\x64" set "MISSING=%MISSING% x64"
if defined MISSING (
    echo.
    echo [WARNING] The following are MISSING from dist\IntraBox:%MISSING%
    echo           OCR or native dependencies may fail at runtime.
    echo [WARNING] Missing:%MISSING%>> "%LOG%"
    goto :fail
)

echo.
echo ============================================================
echo [SUCCESS] Clean rebuild completed.
echo   Output : src\IntraBox\bin\Release\IntraBox.exe
echo   Publish: dist\IntraBox\IntraBox.exe
echo   Log    : build.log
echo ============================================================
echo [SUCCESS] Clean rebuild completed.>> "%LOG%"
echo.
pause
exit /b 0

:buildfail
echo.
echo [FAILED] Build failed. Last lines of build.log:
echo ----------------------------------------------------------
powershell -NoProfile -Command "Get-Content -Tail 25 '%LOG%'"
echo ----------------------------------------------------------
echo Full log: %LOG%
goto :fail

:fail
echo.
echo ============================================================
echo [FAILED] See messages above and build.log for details.
echo ============================================================
echo.
pause
exit /b 1
