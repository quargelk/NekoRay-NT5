@echo off
rem Smoke test of the window code (see UiSmoke.cs). Needs a built NekoRayNT5.exe and xray.exe.
rem Usage: run-ui-smoke.cmd path\to\xray.exe [Debug|Release] [en|ru]
rem Everything runs in a temporary folder with its own data directory; the system proxy is not touched.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe
set XRAY=%~1
set CONFIG=%~2
if "%CONFIG%"=="" set CONFIG=Debug
set APP=%~dp0..\NekoRayNT5\bin\x86\%CONFIG%\NekoRayNT5.exe
if not exist "%APP%" (
    echo Build the solution first: %APP%
    exit /b 2
)
if not exist "%XRAY%" (
    echo xray.exe not found: %XRAY%
    exit /b 2
)
set WORK=%TEMP%\nekoray-nt5-uismoke-%RANDOM%%RANDOM%
mkdir "%WORK%"
copy /y "%APP%" "%WORK%\" >nul
copy /y "%XRAY%" "%WORK%\" >nul
"%CSC%" /nologo /noconfig /platform:x86 /target:exe /warn:4 /warnaserror+ "/out:%WORK%\uismoke.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0UiSmoke.cs"
if errorlevel 1 (
    rmdir /s /q "%WORK%" >nul 2>&1
    exit /b 2
)
"%WORK%\uismoke.exe" %3
set RESULT=%ERRORLEVEL%
rmdir /s /q "%WORK%" >nul 2>&1
exit /b %RESULT%
