@echo off
setlocal

set "PROJECT_ROOT=%~dp0.."
set "PUBLISH_DIR=%PROJECT_ROOT%\bin\Release\net8.0-windows\win-x64\publish"

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
echo Validate exit code: %EXIT_CODE%
echo Config path: %PUBLISH_DIR%\Config.json
popd

pause
exit /b %EXIT_CODE%
