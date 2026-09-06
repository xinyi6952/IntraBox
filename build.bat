@echo off
REM ============================================================
REM  IntraBox build script
REM  Builds the .NET Framework 4.8 WPF project using MSBuild.
REM  Deploy uses robocopy /MIR to mirror bin\Release into
REM  dist\IntraBox, so added/removed dependencies stay in sync.
REM  Every exit path pauses AND writes build.log, so a
REM  double-clicked window never vanishes without a trace.
REM  NOTE: keep this file ASCII-only (English); chcp 65001 makes
REM  tool output (MSBuild/robocopy) render safely in the console.
REM ============================================================
setlocal
cd /d "%~dp0"
echo IntraBox build starting...
chcp 65001 >nul
set "LOG=%~dp0build.log"
echo ==== IntraBox build %DATE% %TIME% ==== > "%LOG%"

set "MSBUILD="
if exist "%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe"

if not defined MSBUILD (
    echo [ERROR] MSBuild not found. Please install Visual Studio or .NET Framework 4.8 Developer Pack.
    echo [ERROR] MSBuild not found.>> "%LOG%"
    goto :fail
)

echo Using MSBuild: %MSBUILD%
echo Using MSBuild: %MSBUILD%>> "%LOG%"
echo.

REM IntraBox stays in the system tray: closing the window does NOT exit the process.
REM If the old process is still running, it locks the exe and the new build cannot be written.
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

echo Building... NuGet restore then compile. Output is live below (also in build.log).
echo --- MSBuild --- >> "%LOG%"
"%MSBUILD%" "src\IntraBox\IntraBox.csproj" /t:"Restore;Build" /p:Configuration=Release /p:NuGetAudit=false /m /v:m /fl "/flp:LogFile=%LOG%;Append;Encoding=UTF-8;Verbosity=minimal"
set "BUILDRC=%ERRORLEVEL%"
if not "%BUILDRC%"=="0" (
    echo [FAILED] Build failed.>> "%LOG%"
    goto :buildfail
)

REM Copy docs first so the mirror below does not treat them as extras to delete.
copy /Y "docs\*.txt" "dist\IntraBox\" >> "%LOG%" 2>&1
REM Mirror bin\Release into dist\IntraBox (copy + delete extras).
REM /XF excludes: pdb + personal data (config/history/fileorganize/undo) + docs txt
REM /XD excludes: the .claude working folder
robocopy "src\IntraBox\bin\Release" "dist\IntraBox" /MIR /NFL /NDL /NJH /NP /R:2 /W:1 /XF *.pdb *.txt config.json history.json fileorganize.json fileorganize-undo.json /XD .claude >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
REM robocopy exit codes 0-7 = success, >=8 = failure
REM cwd-in-dir does NOT block robocopy writes; the real risk is a RUNNING
REM dist\IntraBox\IntraBox.exe locking its DLLs. Detect that directly.
if %RC% GEQ 8 (
    echo [ERROR] robocopy failed, code %RC%.>> "%LOG%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "tools\find-locking-processes.ps1" -Path "%CD%\dist\IntraBox"
    echo ============================================================
    echo  [ERROR] Deploy to dist\IntraBox failed - files in use.
    echo  If IntraBox is running from dist, exit it from the tray first.
    echo ============================================================
    goto :fail
)
echo Synced to dist\IntraBox\ - mirrored from bin\Release.
echo [OK] Synced to dist.>> "%LOG%"

echo.
echo ============================================================
echo [SUCCESS] Build completed.
echo   Output : src\IntraBox\bin\Release\IntraBox.exe
echo   Publish: dist\IntraBox\IntraBox.exe
echo   Log    : build.log
echo ============================================================
echo [SUCCESS] Build completed.>> "%LOG%"
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
