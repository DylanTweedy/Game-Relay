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

pushd "%PUBLISH_DIR%"

if exist "Config.json" del /q "Config.json"
if exist "Registry.json" del /q "Registry.json"
if exist "Logs" rmdir /s /q "Logs"

echo Runtime files reset. Running validate...
Relay.exe validate
set "EXIT_CODE=%ERRORLEVEL%"
echo Exit code: %EXIT_CODE%

popd
pause
exit /b %EXIT_CODE%
