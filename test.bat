@echo off
REM ============================================================
REM  IntraBox test script
REM  Builds the test project and runs all MSTest unit tests.
REM  NOTE: keep this file ASCII-only (English) to avoid encoding issues.
REM ============================================================
setlocal

set "MSBUILD="
if exist "%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"

if not defined MSBUILD (
    echo [ERROR] MSBuild not found.
    pause
    exit /b 1
)

set "VSTEST="
if exist "%ProgramFiles%\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" set "VSTEST=%ProgramFiles%\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
if not defined VSTEST if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" set "VSTEST=%ProgramFiles%\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
if not defined VSTEST if exist "%ProgramFiles%\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" set "VSTEST=%ProgramFiles%\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"

if not defined VSTEST (
    echo [ERROR] vstest.console.exe not found.
    pause
    exit /b 1
)

echo Building test project...
"%MSBUILD%" "tests\IntraBox.Tests\IntraBox.Tests.csproj" /t:Restore;Build /p:Configuration=Release /m
if errorlevel 1 (
    echo.
    echo [FAILED] Test project build failed. See errors above.
    echo.
    pause
    exit /b 1
)

echo.
echo Running tests...
"%VSTEST%" "tests\IntraBox.Tests\bin\Release\IntraBox.Tests.dll"
echo.
pause
exit /b 0
