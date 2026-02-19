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
Relay.exe validate
echo Exit code after validate: %ERRORLEVEL%
Relay.exe manager
echo Exit code after manager: %ERRORLEVEL%
popd

pause
exit /b 0
