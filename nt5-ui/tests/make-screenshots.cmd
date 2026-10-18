@echo off
rem Makes review screenshots of every window from the built program with synthetic data only (see
rem Screenshots.cs), to check texts and layout in both languages. Needs a Release build. Nothing
rem connects; the system proxy is not touched. They show the host Windows: not for the README.
rem Usage: make-screenshots.cmd [output folder] [en|ru]
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe
set PKG=%~dp0..\NekoRayNT5\bin\x86\Release\package
set OUTDIR=%~1
if "%OUTDIR%"=="" set OUTDIR=%TEMP%\nekoray-nt5-review
if not exist "%PKG%\NekoRayNT5.exe" (
    echo Build the Release configuration first: %PKG%
    exit /b 2
)
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set WORK=%TEMP%\nekoray-nt5-screenshots-%RANDOM%%RANDOM%
mkdir "%WORK%"
copy /y "%PKG%\*.*" "%WORK%\" >nul
"%CSC%" /nologo /noconfig /platform:x86 /target:winexe /warn:4 /warnaserror+ "/out:%WORK%\screenshots.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0Screenshots.cs"
if errorlevel 1 exit /b 2
"%WORK%\screenshots.exe" "%OUTDIR%" %2
set RESULT=%ERRORLEVEL%
rmdir /s /q "%WORK%" >nul 2>&1
exit /b %RESULT%
