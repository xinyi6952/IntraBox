@echo off
REM ============================================================
REM  IntraBox clean rebuild script
REM  Fully removes build caches (obj/bin) and the old dist
REM  package (including personal config.json/history.json), then
REM  rebuilds and redeploys from scratch.
REM  Difference from build.bat: this does a FULL clean rebuild,
REM  so the resulting dist is guaranteed free of stale artifacts.
REM  NOTE: keep this file ASCII-only (English) to avoid encoding
REM  issues when cmd parses it under the GBK code page.
REM ============================================================
setlocal

set "MSBUILD="
if exist "%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe"

if not defined MSBUILD (
    echo [ERROR] MSBuild not found.
    pause
    exit /b 1
)

echo Using MSBuild: %MSBUILD%
echo.

REM IntraBox stays in the system tray: closing the window does NOT exit the process.
REM If the old process is still running, it locks the exe and the new build cannot be written.
tasklist /FI "IMAGENAME eq IntraBox.exe" | "%SystemRoot%\System32\find.exe" /I "IntraBox.exe" >nul
if not errorlevel 1 (
    echo [ERROR] IntraBox is still running. Right-click the tray icon and choose Exit first, then run this script again.
    pause
    exit /b 1
)

REM ---- 1. delete build caches (obj/bin) ----
echo [1/4] Deleting build caches (obj, bin)...
if exist "src\IntraBox\obj" rmdir /S /Q "src\IntraBox\obj"
if exist "src\IntraBox\bin" rmdir /S /Q "src\IntraBox\bin"
if exist "tests\IntraBox.Tests\obj" rmdir /S /Q "tests\IntraBox.Tests\obj"
if exist "tests\IntraBox.Tests\bin" rmdir /S /Q "tests\IntraBox.Tests\bin"

REM ---- 2. delete old dist package (incl. personal config.json/history.json) ----
echo [2/4] Deleting old dist package...
if exist "dist\IntraBox" rmdir /S /Q "dist\IntraBox"

REM ---- 3. clean build ----
echo [3/4] Building...
"%MSBUILD%" "src\IntraBox\IntraBox.csproj" /t:Restore;Build /p:Configuration=Release /m
if errorlevel 1 (
    echo.
    echo [FAILED] Build failed. See the error messages above.
    echo.
    pause
    exit /b 1
)

REM ---- 4. redeploy to a fresh dist\IntraBox\ ----
echo [4/4] Deploying to dist\IntraBox\...
if not exist "dist" mkdir "dist"
if not exist "dist\IntraBox" mkdir "dist\IntraBox"
copy /Y "src\IntraBox\bin\Release\IntraBox.exe" "dist\IntraBox\IntraBox.exe" >nul
if exist "src\IntraBox\bin\Release\IntraBox.exe.config" copy /Y "src\IntraBox\bin\Release\IntraBox.exe.config" "dist\IntraBox\IntraBox.exe.config" >nul
if exist "src\IntraBox\bin\Release\*.dll" copy /Y "src\IntraBox\bin\Release\*.dll" "dist\IntraBox\" >nul
if exist "src\IntraBox\bin\Release\tessdata\" xcopy /E /I /Y "src\IntraBox\bin\Release\tessdata" "dist\IntraBox\tessdata" >nul
if exist "src\IntraBox\bin\Release\x86\" xcopy /E /I /Y "src\IntraBox\bin\Release\x86" "dist\IntraBox\x86" >nul
if exist "src\IntraBox\bin\Release\x64\" xcopy /E /I /Y "src\IntraBox\bin\Release\x64" "dist\IntraBox\x64" >nul
REM Sync localization resource folders (from zxing/tesseract packages).
for %%d in (en es fr ja ko pt zh-Hans zh-Hant) do (
    if exist "src\IntraBox\bin\Release\%%d\" xcopy /E /I /Y "src\IntraBox\bin\Release\%%d" "dist\IntraBox\%%d" >nul
)
copy /Y "docs\*.txt" "dist\IntraBox\" >nul

echo.
echo [SUCCESS] Clean rebuild completed.
echo   Output : src\IntraBox\bin\Release\IntraBox.exe
echo   Publish: dist\IntraBox\IntraBox.exe
echo.
pause
exit /b 0
