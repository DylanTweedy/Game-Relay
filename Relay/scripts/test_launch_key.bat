@echo off
setlocal

set "PUBLISH_DIR=%~dp0..\bin\Release\net8.0-windows\win-x64\publish"
if not exist "%PUBLISH_DIR%\Relay.exe" (
  echo Relay.exe not found at:
  echo %PUBLISH_DIR%\Relay.exe
  echo Run scripts\clean_and_publish.bat first.
  pause
  exit /b 1
)

if "%RELAY_KEY%"=="" (
  set /p RELAY_KEY=Enter game GUID: 
)

pushd "%PUBLISH_DIR%"
Relay.exe launch --key %RELAY_KEY%
set "EXIT_CODE=%ERRORLEVEL%"
echo Exit code: %EXIT_CODE%
popd

pause
exit /b %EXIT_CODE%
