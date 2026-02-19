@echo off
setlocal enabledelayedexpansion

set "PUBLISH_DIR=%~dp0..\bin\Release\net8.0-windows\win-x64\publish"
if not exist "%PUBLISH_DIR%\Relay.exe" (
  echo Relay.exe not found at:
  echo %PUBLISH_DIR%\Relay.exe
  echo Run scripts\clean_and_publish.bat first.
  pause
  exit /b 1
)

pushd "%PUBLISH_DIR%"
Relay.exe validate
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo Exit code: %EXIT_CODE%

set "NEWEST_LOG="
for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  set "NEWEST_LOG=%%F"
  goto :showlog
)

echo No relay log file found in Logs\
goto :done

:showlog
echo.
echo Latest log: Logs\%NEWEST_LOG%
powershell -NoProfile -Command "Get-Content 'Logs\%NEWEST_LOG%' -Tail 80"

:done
popd
pause
exit /b %EXIT_CODE%
