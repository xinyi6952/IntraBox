@echo off
REM ============================================================
REM  IntraBox build script
REM  Builds the .NET Framework 4.8 WPF project using MSBuild.
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
    echo [ERROR] MSBuild not found. Please install Visual Studio or .NET Framework 4.8 Developer Pack.
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

"%MSBUILD%" "src\IntraBox\IntraBox.csproj" /t:Restore;Build /p:Configuration=Release /m
if errorlevel 1 (
    echo.
    echo [FAILED] Build failed. See the error messages above.
    echo.
    pause
    exit /b 1
)

if exist "dist\IntraBox\" (
    REM Clean old exe/dll first, so removed dependencies do not linger in dist.
    if exist "dist\IntraBox\*.dll" del /Q "dist\IntraBox\*.dll" >nul
    if exist "dist\IntraBox\*.exe" del /Q "dist\IntraBox\*.exe" >nul
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
    echo Synced to dist\IntraBox\
)

echo.
echo [SUCCESS] Build completed.
echo   Output : src\IntraBox\bin\Release\IntraBox.exe
echo   Publish: dist\IntraBox\IntraBox.exe
echo.
pause
exit /b 0
