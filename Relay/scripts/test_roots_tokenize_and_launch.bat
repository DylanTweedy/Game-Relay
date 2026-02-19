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

set "TEST_KEY=33333333-3333-3333-3333-333333333333"
set "CACHE_ROOT=%PUBLISH_DIR%\CacheRootTest"
set "LB_ROOT=%PUBLISH_DIR%\LaunchBoxRootTest"
set "SCAN_ROOT=%PUBLISH_DIR%\ScanRootsTest"

pushd "%PUBLISH_DIR%"

if exist "Logs\relay_*.log" del /q "Logs\relay_*.log" >nul 2>nul

powershell -NoProfile -Command ^
  "$cfg = [pscustomobject]@{ " ^
  "  SchemaVersion = 1; " ^
  "  LaunchBox = [pscustomobject]@{ RootPath=''; PreferredPlatforms=@() }; " ^
  "  Paths = [pscustomobject]@{ ShortcutImportRoot='%PUBLISH_DIR%\ShortcutImport'; ShortcutOutputRoot='%PUBLISH_DIR%\ShortcutsOut'; GamesRoot='%PUBLISH_DIR%'; CacheRoot='%CACHE_ROOT%'; LaunchBoxRoot='%LB_ROOT%'; TempRoot='%PUBLISH_DIR%\Temp'; ScanRoots=@('%SCAN_ROOT%') }; " ^
  "  Launch = [pscustomobject]@{ ActuallyLaunch=$true }; " ^
  "  Cache = [pscustomobject]@{ Enabled=$true; MaxBytes=0; PurgePolicy='LRU'; KeepPinned=$true }; " ^
  "  Overlay = [pscustomobject]@{ Enabled=$true; FadeOutMs=300 }; " ^
  "  Scanning = [pscustomobject]@{ Enabled=$true; MinExeBytes=524288; ExcludedExePatterns=@(); ExcludedFolderNames=@() }; " ^
  "  ScannerRules = [pscustomobject]@{ ExeIgnoreNamePatterns=@(); ExeIgnoreFolderNamePatterns=@() }; " ^
  "  Diagnostics = [pscustomobject]@{ VerboseLogging=$false } " ^
  "}; " ^
  "$cfg | ConvertTo-Json -Depth 12 | Set-Content 'Config.json' -Encoding utf8"

powershell -NoProfile -Command ^
  "$path='Registry.json'; " ^
  "$json=[pscustomobject]@{ SchemaVersion=1; HiddenExecutables=@(); Games=@() }; " ^
  "$entry=[pscustomobject]@{ " ^
  "  GameKey='%TEST_KEY%'; LaunchKey=''; ImportedFromShortcut=$false; ImportedShortcutIconLocation=''; DisplayName='Roots Token Launch Test'; " ^
  "  Install=[pscustomobject]@{ GameFolderPath=''; BaseFolder=''; ExePath=''; ToolExePaths=@(); Args='validate'; WorkingDir='' }; " ^
  "  Launch=[pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=[pscustomobject]@{ TargetPath='{GamesRoot}\\Relay.exe'; Arguments='validate'; WorkingDirectory='{GamesRoot}' }; Tools=@{} }; " ^
  "  Stats=[pscustomobject]@{ EstimatedBytes=0; LastPlayedUtc=''; LastValidatedUtc=''; LastResult='Unknown' }; LaunchBoxLink=$null " ^
  "}; " ^
  "$json.Games += $entry; " ^
  "$json | ConvertTo-Json -Depth 12 | Set-Content $path -Encoding utf8"

echo Running validate...
Relay.exe validate
echo Validate exit code: %ERRORLEVEL%

echo Running tokenized launch with key %TEST_KEY%...
Relay.exe launch --key %TEST_KEY%
set "EXIT_CODE=%ERRORLEVEL%"
echo Launch exit code: %EXIT_CODE%

set "NEWEST_LOG="
for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  set "NEWEST_LOG=%%F"
  goto :checklog
)

echo No relay log file found in Logs\
goto :done

:checklog
echo Latest log: Logs\%NEWEST_LOG%
powershell -NoProfile -Command "Get-Content 'Logs\%NEWEST_LOG%' -Tail 140"
powershell -NoProfile -Command ^
  "$hit = Select-String -Path 'Logs\%NEWEST_LOG%' -Pattern 'Launch resolved target exists' -SimpleMatch; " ^
  "if ($hit) { Write-Host 'PASS: resolved target exists logged.'; exit 0 } else { Write-Host 'FAIL: expected resolved target log not found.'; exit 2 }"
if errorlevel 1 (
  set "EXIT_CODE=2"
)

:done
popd
pause
exit /b %EXIT_CODE%
