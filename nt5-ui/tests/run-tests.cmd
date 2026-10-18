@echo off
rem Builds and runs the automatic checks with the C# 2.0 compiler of .NET Framework 2.0.
rem Usage: run-tests.cmd [path\to\xray.exe [TestA,TestB]]   TestA,TestB: run only these sections (names of the Test... methods)
rem Without xray.exe the configuration and core checks are skipped (and reported as skipped).
rem Optional and separate from the Visual Studio solution; works on Windows XP as well.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe
if not exist "%CSC%" (
    echo .NET Framework 2.0 compiler not found: %CSC%
    exit /b 2
)
set XRAY=%~1
if "%XRAY%"=="" set XRAY=%XRAY_EXE%
if "%XRAY%"=="" set XRAY=%~dp0..\NekoRayNT5\bin\x86\Release\xray.exe
set SRC=%~dp0..\NekoRayNT5
set OUT=%TEMP%\nekoray-nt5-selftest.exe
"%CSC%" /nologo /noconfig /platform:x86 /target:exe /warn:4 /warnaserror+ "/out:%OUT%" ^
  /r:System.dll /r:System.Xml.dll /r:System.Windows.Forms.dll "/resource:%SRC%\Lang\ru.txt,NekoRayNT5.Lang.ru.txt" ^
  "%SRC%\App\*.cs" "%SRC%\Core\*.cs" "%SRC%\Model\*.cs" "%SRC%\Platform\*.cs" "%SRC%\Services\*.cs" "%~dp0SelfTest*.cs"
if errorlevel 1 exit /b 2
rem the translation check reads the sources (every text of the program must be in Lang\ru.txt)
set NT5_SRC=%SRC%
"%OUT%" "%XRAY%" %2 %3 %4 %5 %6 %7 %8 %9
set RESULT=%ERRORLEVEL%
del "%OUT%" >nul 2>&1
exit /b %RESULT%
