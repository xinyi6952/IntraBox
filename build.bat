@echo off
REM ============================================================
REM  IntraBox build script
REM  Default: keep user data, bump version, Restore;Rebuild,
REM  robocopy /MIR into dist\IntraBox (preserves datapath.txt).
REM  test / 2: clean test package into dist\IntraBox.test with
REM  isolated Data.test; does not bump version; does not touch
REM  user Data or dist\IntraBox\datapath.txt.
REM  norun / /norun / menu N: skip launching the new exe.
REM  Menu Y or Enter: start the new exe from the publish folder.
REM  Success then exits so a double-clicked window closes.
REM  Fail/abort/launch-fail still pause and write build.log.
REM  NOTE: keep this file ASCII-only (English); chcp 65001 makes
REM  tool output (MSBuild/robocopy) render safely in the console.
REM ============================================================
setlocal
cd /d "%~dp0"
chcp 65001 >nul

set "MODE="
set "NORUN=0"
set "HASARG=0"
if not "%~1"=="" set "HASARG=1"

:argloop
if "%~1"=="" goto :argdone
if /i "%~1"=="test" set "MODE=test"
if /i "%~1"=="2" set "MODE=test"
if /i "%~1"=="1" set "MODE=keep"
if /i "%~1"=="norun" set "NORUN=1"
if /i "%~1"=="/norun" set "NORUN=1"
if /i "%~1"=="-norun" set "NORUN=1"
shift
goto :argloop
:argdone

if "%HASARG%"=="0" goto :menu
if not defined MODE set "MODE=keep"
goto :startbuild

:menu
echo.
echo Select package mode:
echo   [1] Keep user data    - default
echo   [2] Clean test package
set /p CHOICE=Your choice [1/2]: 
if "%CHOICE%"=="2" (
    set "MODE=test"
) else (
    set "MODE=keep"
)
echo.
echo Launch the new exe after build?
echo   [Y] Yes - default
echo   [N] No
set /p LAUNCH=Launch [Y/N]: 
if /i "%LAUNCH%"=="n" set "NORUN=1"
if /i "%LAUNCH%"=="no" set "NORUN=1"

:startbuild
if /i "%MODE%"=="test" (
    set "DEST=dist\IntraBox.test"
    set "MODELABEL=clean test package"
) else (
    set "MODE=keep"
    set "DEST=dist\IntraBox"
    set "MODELABEL=keep user data"
)

echo IntraBox build starting...
echo Mode: %MODELABEL%
if "%NORUN%"=="1" echo Launch after build: no
set "LOG=%~dp0build.log"
echo ==== IntraBox build %DATE% %TIME% mode=%MODE% norun=%NORUN% ==== > "%LOG%"
echo Mode: %MODELABEL%>> "%LOG%"

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
REM If it is running it locks the exe; ask, then stop it after Yes (default is No).
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\stop-intrabox.ps1"
if errorlevel 2 (
    echo [ERROR] Could not stop IntraBox.exe. Close it from the tray and retry.
    echo [ERROR] Could not stop IntraBox.exe.>> "%LOG%"
    goto :fail
)
if errorlevel 1 (
    echo Aborted. IntraBox is still running.
    echo [ABORTED] IntraBox still running, user cancelled.>> "%LOG%"
    echo.
    pause
    exit /b 0
)

set "APPVER="
if /i "%MODE%"=="test" goto :readver

echo Bumping pack version...
echo --- Version --- >> "%LOG%"
for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -File "tools\bump-version.ps1"`) do set "APPVER=%%V"
if not defined APPVER (
    echo [ERROR] Version bump failed.
    echo [ERROR] Version bump failed.>> "%LOG%"
    goto :fail
)
echo Pack version: %APPVER%
echo Pack version: %APPVER%>> "%LOG%"
echo.
goto :wipeobj

:readver
echo Skipping version bump (test package).
echo --- Version (unchanged) --- >> "%LOG%"
for /f "usebackq delims=" %%V in ("version.txt") do set "APPVER=%%V"
if not defined APPVER set "APPVER=(unknown)"
echo Pack version (unchanged): %APPVER%
echo Pack version (unchanged): %APPVER%>> "%LOG%"
echo.

:wipeobj
echo [1/5] Deleting build caches - obj, bin...
if exist "src\IntraBox\obj" rmdir /S /Q "src\IntraBox\obj"
if exist "src\IntraBox\bin" rmdir /S /Q "src\IntraBox\bin"
if exist "tests\IntraBox.Tests\obj" rmdir /S /Q "tests\IntraBox.Tests\obj"
if exist "tests\IntraBox.Tests\bin" rmdir /S /Q "tests\IntraBox.Tests\bin"

echo [2/5] Building... NuGet restore then Rebuild. Output is live below (also in build.log).
echo --- MSBuild --- >> "%LOG%"
REM Use explicit Rebuild (not Restore;Build): after wiping obj, Restore alone does
REM not re-register Tesseract's native x86/x64 assets, so a plain Build skips them.
"%MSBUILD%" "src\IntraBox\IntraBox.csproj" /t:"Restore;Rebuild" /p:Configuration=Release /p:NuGetAudit=false /m /v:m /fl "/flp:LogFile=%LOG%;Append;Encoding=UTF-8;Verbosity=minimal"
set "BUILDRC=%ERRORLEVEL%"
if not "%BUILDRC%"=="0" (
    echo [FAILED] Build failed.>> "%LOG%"
    goto :buildfail
)

echo [3/5] Deploying to %DEST%\...
if not exist "dist" mkdir "dist"
if not exist "%DEST%" mkdir "%DEST%"
REM Copy docs first so the mirror below does not treat them as extras to delete.
copy /Y "docs\*.txt" "%DEST%\" >> "%LOG%" 2>&1
REM /MIR mirrors; /XF *.txt keeps dest datapath.txt and the docs just copied.
REM Do NOT rmdir the dest folder: that would drop dist\IntraBox\datapath.txt.
robocopy "src\IntraBox\bin\Release" "%DEST%" /MIR /NFL /NDL /NJH /NP /R:2 /W:1 /XF *.pdb *.txt config.json history.json fileorganize.json fileorganize-undo.json launcher.json /XD .claude WebView2Runtime >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
REM robocopy exit codes 0-7 = success, >=8 = failure
if %RC% GEQ 8 (
    echo [ERROR] robocopy failed, code %RC%.>> "%LOG%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "tools\find-locking-processes.ps1" -Path "%CD%\%DEST%"
    echo ============================================================
    echo  [ERROR] Deploy to %DEST% failed - files in use.
    echo  If IntraBox is running from dist, exit it from the tray first.
    echo ============================================================
    goto :fail
)
echo Synced to %DEST%\ - mirrored from bin\Release.
echo [OK] Synced to %DEST%.>> "%LOG%"

REM Prefer the CAB. Expanded WebView2Runtime is only a fallback when no CAB exists.
set "WVCAB="
for %%F in ("redist\Microsoft.WebView2.FixedVersionRuntime*.x64.cab") do if exist "%%~fF" set "WVCAB=%%~fF"
if defined WVCAB goto :packcab
if exist "redist\WebView2Runtime\msedgewebview2.exe" goto :packexpanded
echo [WARNING] WebView2 CAB not in package. Win10/11 Markdown preview will use IE until you fetch and rebuild.
echo [WARNING] Run tools\fetch-webview2-runtime.ps1 then rebuild.
echo [WARNING] WebView2 CAB missing.>> "%LOG%"
goto :packdone

:packcab
echo Copying WebView2 CAB into package...
copy /Y "%WVCAB%" "%DEST%\WebView2Runtime.cab" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo [WARNING] WebView2 CAB copy failed. Preview will use IE.
    echo [WARNING] WebView2 CAB copy failed.>> "%LOG%"
    goto :packdone
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ver=$null; $n=[IO.Path]::GetFileName('%WVCAB%'); if ($n -match '(\d+\.\d+\.\d+\.\d+)') { $ver=$Matches[1] }; if (-not $ver -and (Test-Path 'redist\webview2-x64.url.txt')) { $u=[IO.File]::ReadAllText('redist\webview2-x64.url.txt'); if ($u -match 'FixedVersionRuntime\.(\d+\.\d+\.\d+\.\d+)') { $ver=$Matches[1] } }; if ($ver) { [IO.File]::WriteAllText('%DEST%\WebView2Runtime.cab.ver', $ver, [Text.Encoding]::ASCII) }"
if exist "%DEST%\WebView2Runtime" rmdir /S /Q "%DEST%\WebView2Runtime"
if exist "%DEST%\WebView2Runtime" (
    echo [WARNING] Could not remove expanded WebView2Runtime. Close IntraBox and rebuild to shrink the package.
    echo [WARNING] Expanded WebView2Runtime still in %DEST%.>> "%LOG%"
) else (
    echo [OK] WebView2 CAB packaged. Runtime extracts on first preview.
    echo [OK] WebView2 CAB packaged.>> "%LOG%"
)
goto :packdone

:packexpanded
echo Copying expanded WebView2 Fixed Version from redist\WebView2Runtime...
if not exist "%DEST%\WebView2Runtime" mkdir "%DEST%\WebView2Runtime"
robocopy "redist\WebView2Runtime" "%DEST%\WebView2Runtime" /E /NFL /NDL /NJH /NP /R:1 /W:1 >> "%LOG%" 2>&1
if exist "%DEST%\WebView2Runtime\msedgewebview2.exe" (
    echo [OK] WebView2Runtime packaged.>> "%LOG%"
) else (
    echo [WARNING] WebView2Runtime copy failed.
    echo [WARNING] WebView2Runtime copy failed.>> "%LOG%"
)

:packdone

if /i not "%MODE%"=="test" goto :verify

REM Write datapath.txt AFTER robocopy: /MIR would delete a file the source lacks.
REM Isolated test root only. Never read or delete dist\IntraBox\datapath.txt.
echo [4/5] Writing isolated datapath.txt and clearing Data.test...
(echo %LOCALAPPDATA%\IntraBox\Data.test)> "%DEST%\datapath.txt"
if exist "%LOCALAPPDATA%\IntraBox\Data.test" rmdir /S /Q "%LOCALAPPDATA%\IntraBox\Data.test" 2>nul
echo Isolated data: %LOCALAPPDATA%\IntraBox\Data.test>> "%LOG%"
goto :verify2

:verify
echo [4/5] Verifying package completeness...
goto :verifybody

:verify2
echo [5/5] Verifying package completeness...

:verifybody
echo [OK] Verify %DEST%>> "%LOG%"
set "MISSING="
if not exist "%DEST%\IntraBox.exe" set "MISSING=%MISSING% IntraBox.exe"
if not exist "%DEST%\tessdata" set "MISSING=%MISSING% tessdata"
if not exist "%DEST%\x86" set "MISSING=%MISSING% x86"
if not exist "%DEST%\x64" set "MISSING=%MISSING% x64"
if not "%MISSING%"=="" (
    echo.
    echo [WARNING] The following are MISSING from %DEST%:%MISSING%
    echo           OCR or native dependencies may fail at runtime.
    echo [WARNING] Missing:%MISSING%>> "%LOG%"
    goto :fail
)

if "%NORUN%"=="1" goto :skiplaunch
echo Launching %DEST%\IntraBox.exe
echo [OK] Launching %DEST%\IntraBox.exe>> "%LOG%"
start "" /D "%DEST%" IntraBox.exe
goto :success

:skiplaunch
echo Skipping launch - norun
echo [OK] Launch skipped.>> "%LOG%"

:success
echo.
echo ============================================================
echo [SUCCESS] Build completed.
echo   Mode   : %MODELABEL%
echo   Version: %APPVER%
echo   Output : src\IntraBox\bin\Release\IntraBox.exe
echo   Publish: %DEST%\IntraBox.exe
echo   Log    : build.log
echo ============================================================
echo [SUCCESS] Build completed. dest=%DEST%>> "%LOG%"
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
