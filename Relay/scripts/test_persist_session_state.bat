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

set "SESSION_FILE=%PUBLISH_DIR%\SessionState.json"
if exist "%SESSION_FILE%" del /f /q "%SESSION_FILE%"
echo Deleted session file (if present): %SESSION_FILE%
echo.
echo First run: perform scans/imports, then close the manager window.
pushd "%PUBLISH_DIR%"
Relay.exe manager
echo.
echo Second run: confirm previous lists are restored, then close the manager window.
Relay.exe manager
set "EXIT_CODE=%ERRORLEVEL%"

echo.
echo Session file location: %SESSION_FILE%
if exist "%SESSION_FILE%" (
  echo Session file exists.
) else (
  echo Session file missing.
)

set "NEWEST_LOG="
for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  set "NEWEST_LOG=%%F"
  goto :showlog
)

echo No relay log file found in Logs\
goto :done

:showlog
echo Latest log: Logs\%NEWEST_LOG%
powershell -NoProfile -Command "Get-Content 'Logs\%NEWEST_LOG%' -Tail 120"

:done
popd
pause
exit /b %EXIT_CODE%
