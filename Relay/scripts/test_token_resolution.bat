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

set "TEST_KEY=22222222-2222-2222-2222-222222222222"
pushd "%PUBLISH_DIR%"

powershell -NoProfile -Command ^
  "$path='Registry.json'; " ^
  "$json=Get-Content $path -Raw | ConvertFrom-Json; " ^
  "$json.Games = @($json.Games | Where-Object { $_.GameKey -ne '%TEST_KEY%' }); " ^
  "$entry=[pscustomobject]@{ " ^
  "  GameKey='%TEST_KEY%'; LaunchKey=''; ImportedFromShortcut=$false; ImportedShortcutIconLocation=''; DisplayName='Token Launch Test'; " ^
  "  Install=[pscustomobject]@{ GameFolderPath=''; BaseFolder=''; ExePath=''; ToolExePaths=@(); Args='validate'; WorkingDir='' }; " ^
  "  Launch=[pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=[pscustomobject]@{ TargetPath='{RelayDir}\\Relay.exe'; Arguments='validate'; WorkingDirectory='{RelayDir}' }; Tools=@{} }; " ^
  "  Stats=[pscustomobject]@{ EstimatedBytes=0; LastPlayedUtc=''; LastValidatedUtc=''; LastResult='Unknown' }; LaunchBoxLink=$null " ^
  "}; " ^
  "$json.Games += $entry; " ^
  "$json | ConvertTo-Json -Depth 12 | Set-Content $path"

echo Running tokenized launch test with key %TEST_KEY%...
Relay.exe launch --key %TEST_KEY%
set "EXIT_CODE=%ERRORLEVEL%"
echo Exit code: %EXIT_CODE%

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
