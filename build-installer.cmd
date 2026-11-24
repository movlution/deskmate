@echo off
rem Builds release\Deskmate-Setup.exe: the per-user installer around Deskmate.exe (Inno Setup 6)
rem Optional: build-installer.cmd [Deskmate.exe to package]   (default: builds a fresh one)
setlocal
set ROOT=%~dp0
set EXE=%~1
if "%EXE%"=="" (
  call "%ROOT%build.cmd" || exit /b 1
  set EXE=%ROOT%Deskmate.exe
)

set ISCC=
for %%P in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do if exist %%P set ISCC=%%P
if not defined ISCC for /f "delims=" %%P in ('where iscc 2^>nul') do set ISCC="%%P"
if not defined ISCC (
  echo Inno Setup 6 tidak ditemukan. Pasang: winget install JRSoftware.InnoSetup
  exit /b 1
)

%ISCC% /Q "/DSourceExe=%EXE%" "%ROOT%installer\Deskmate.iss"
if errorlevel 1 (
  echo Installer gagal.
  exit /b 1
)
echo Installer OK: %ROOT%release\Deskmate-Setup.exe
