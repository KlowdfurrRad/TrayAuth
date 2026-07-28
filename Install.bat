@echo off
setlocal

rem Double-clickable front door for building from source: compiles TrayAuth, packages it with
rem Inno Setup, and runs the resulting installer.
rem
rem If you just want to install TrayAuth, use the Setup.exe from the Releases page instead -
rem it needs neither the .NET SDK nor Inno Setup.

echo.
echo  TrayAuth - build and install
echo  ============================
echo.

cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Installer -SelfContained
if errorlevel 1 goto :failed

echo.
echo  Launching the installer...
echo.

for %%F in ("%~dp0dist\TrayAuth-Setup-*.exe") do set "SETUP=%%F"
if not defined SETUP goto :failed

start "" "%SETUP%"

echo  Done - follow the installer window.
echo.
pause
exit /b 0

:failed
echo.
echo  Something went wrong - see the messages above.
echo.
pause
exit /b 1
