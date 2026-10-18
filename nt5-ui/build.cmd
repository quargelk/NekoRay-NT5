@echo off
rem Optional command-line build. The normal way is Build Solution in Visual Studio 2010.
rem Usage: build.cmd [Release|Debug]
setlocal
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release
set MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%MSBUILD%" (
    echo MSBuild 4.0 not found: %MSBUILD%
    exit /b 1
)
"%MSBUILD%" "%~dp0NekoRayNT5.sln" /nologo /t:Rebuild /p:Configuration=%CONFIG% /p:Platform=x86
exit /b %ERRORLEVEL%
