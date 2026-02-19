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

set "IMPORT_DIR=%PUBLISH_DIR%\ShortcutImport"
if not exist "%IMPORT_DIR%" mkdir "%IMPORT_DIR%"
del /q "%IMPORT_DIR%\BattleNet_*.lnk" 2>nul

powershell -NoProfile -Command ^
  "$ws=New-Object -ComObject WScript.Shell; " ^
  "$s1=$ws.CreateShortcut('%IMPORT_DIR%\BattleNet_OSI.lnk'); " ^
  "$s1.TargetPath='G:\Battle.net\Battle.net.exe'; $s1.Arguments='--exec=""launch OSI""'; $s1.WorkingDirectory='G:\Battle.net'; $s1.Save(); " ^
  "$s2=$ws.CreateShortcut('%IMPORT_DIR%\BattleNet_WTCG.lnk'); " ^
  "$s2.TargetPath='G:\Battle.net\Battle.net.exe'; $s2.Arguments='--exec=""launch WTCG""'; $s2.WorkingDirectory='G:\Battle.net'; $s2.Save();"

echo Created test shortcuts in:
echo %IMPORT_DIR%
echo.
echo Validation steps in Relay Manager:
echo 1) Import Shortcuts tab: Scan Shortcuts
echo 2) Confirm both BattleNet shortcuts show Status=OK (not collapsed)
echo 3) Add both to registry
echo 4) Review ^& Export tab: confirm two distinct game rows remain
echo.
echo Launching manager...
pushd "%PUBLISH_DIR%"
Relay.exe manager
set "EXIT_CODE=%ERRORLEVEL%"

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
