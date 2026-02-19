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

echo Launching Relay Manager lock test.
echo In the app: run Scan Shortcuts / Scan Games and perform a few UI edits, then close.
pushd "%PUBLISH_DIR%"
Relay.exe manager
set "EXIT_CODE=%ERRORLEVEL%"

set "NEWEST_LOG="
for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  set "NEWEST_LOG=%%F"
  goto :checklog
)

echo No relay log file found in Logs\
goto :done

:checklog
echo Latest log: Logs\%NEWEST_LOG%
powershell -NoProfile -Command ^
  "$log='Logs\%NEWEST_LOG%'; " ^
  "$hits=Select-String -Path $log -Pattern 'SessionState.json.*being used by another process'; " ^
  "if($hits){Write-Host 'FAIL: session file lock IOException found.'; $hits | ForEach-Object { $_.Line }; exit 2} " ^
  "else{Write-Host 'PASS: no session file lock IOException found in latest log.'; exit 0}"
set "CHECK_CODE=%ERRORLEVEL%"

:done
popd
pause
if not "%CHECK_CODE%"=="" exit /b %CHECK_CODE%
exit /b %EXIT_CODE%
