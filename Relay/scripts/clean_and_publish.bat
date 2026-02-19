@echo off
setlocal

set "PROJECT_ROOT=%~dp0.."
set "PUBLISH_DIR=%PROJECT_ROOT%\bin\Release\net8.0-windows\win-x64\publish"

pushd "%PROJECT_ROOT%"

echo Running dotnet clean...
dotnet clean -c Release
if errorlevel 1 (
  echo dotnet clean failed with exit code %ERRORLEVEL%.
  popd
  pause
  exit /b %ERRORLEVEL%
)

if exist "%PUBLISH_DIR%" (
  echo Deleting publish folder...
  rmdir /s /q "%PUBLISH_DIR%"
)

echo Running dotnet publish...
dotnet publish -c Release -r win-x64
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
  echo Publish succeeded.
) else (
  echo Publish failed with exit code %EXIT_CODE%.
)

popd
pause
exit /b %EXIT_CODE%
