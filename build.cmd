@echo off
rem Builds Deskmate.exe (.NET 10, self-contained, single file) with the .NET SDK
rem Optional: build.cmd [output.exe]
setlocal
set ROOT=%~dp0
set OUT=%~1
if "%OUT%"=="" set OUT=%ROOT%Deskmate.exe
set STAGE=%ROOT%artifacts\publish

dotnet publish "%ROOT%Deskmate.csproj" -c Release --nologo -v q -o "%STAGE%"
if errorlevel 1 (
  echo Build gagal.
  exit /b 1
)
copy /y "%STAGE%\Deskmate.exe" "%OUT%" >nul
if errorlevel 1 (
  echo Build gagal: tidak bisa menyalin ke %OUT%
  exit /b 1
)
echo Build OK: %OUT%
