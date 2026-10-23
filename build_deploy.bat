@echo off
rem Builds CalcStudioEPM (Release, x64). Every build raises the build number in
rem CalcStudioEPM\BuildNumber.txt; the output is CalcStudioEPM\bin\x64\Release\net48\.
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe not found - install Visual Studio 2022 or later, or the Build Tools, with MSBuild.
    exit /b 1
)
set "MSBUILD="
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do if not defined MSBUILD set "MSBUILD=%%i"
if not defined MSBUILD (
    echo MSBuild.exe not found.
    exit /b 1
)
"%MSBUILD%" "%~dp0CalcStudioEPM\CalcStudioEPM.csproj" /restore /p:Configuration=Release /p:Platform=x64 /t:Build /v:minimal /nologo
exit /b %ERRORLEVEL%
