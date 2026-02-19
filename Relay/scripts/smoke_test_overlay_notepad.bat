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

set "TEST_KEY=66666666-6666-6666-6666-666666666666"

pushd "%PUBLISH_DIR%"

powershell -NoProfile -Command ^
  "$cfg=[pscustomobject]@{ " ^
  "  SchemaVersion=1; " ^
  "  LaunchBox=[pscustomobject]@{ RootPath=''; PreferredPlatforms=@() }; " ^
  "  Paths=[pscustomobject]@{ ShortcutImportRoot='%PUBLISH_DIR%\ShortcutImport'; ShortcutOutputRoot='%PUBLISH_DIR%\ShortcutsOut'; GamesRoot=$env:WINDIR + '\System32'; CacheRoot='%PUBLISH_DIR%\Cache'; LaunchBoxRoot=''; TempRoot='%PUBLISH_DIR%\Temp'; ScanRoots=@('%PUBLISH_DIR%\ScanRoots') }; " ^
  "  Launch=[pscustomobject]@{ ActuallyLaunch=$true }; " ^
  "  Cache=[pscustomobject]@{ Enabled=$true; MaxBytes=0; PurgePolicy='LRU'; KeepPinned=$true }; " ^
  "  Overlay=[pscustomobject]@{ Enabled=$true; FadeOutMs=300; AutoCloseSeconds=3; ShowDetails=$true }; " ^
  "  Scanning=[pscustomobject]@{ Enabled=$true; MinExeBytes=524288; ExcludedExePatterns=@(); ExcludedFolderNames=@() }; " ^
  "  ScannerRules=[pscustomobject]@{ ExeIgnoreNamePatterns=@(); ExeIgnoreFolderNamePatterns=@() }; " ^
  "  Diagnostics=[pscustomobject]@{ VerboseLogging=$false } " ^
  "}; " ^
  "$cfg | ConvertTo-Json -Depth 12 | Set-Content 'Config.json' -Encoding utf8"

powershell -NoProfile -Command ^
  "$reg=[pscustomobject]@{ SchemaVersion=1; HiddenExecutables=@(); Games=@() }; " ^
  "$entry=[pscustomobject]@{ " ^
  "  GameKey='%TEST_KEY%'; LaunchKey=''; ImportedFromShortcut=$false; ImportedShortcutIconLocation=''; DisplayName='Overlay Notepad Smoke'; " ^
  "  Install=[pscustomobject]@{ GameFolderPath=''; BaseFolder=''; ExePath=''; ToolExePaths=@(); Args=''; WorkingDir='' }; " ^
  "  Launch=[pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=[pscustomobject]@{ TargetPath='{GamesRoot}\notepad.exe'; Arguments=''; WorkingDirectory='{GamesRoot}' }; Tools=@{} }; " ^
  "  Stats=[pscustomobject]@{ EstimatedBytes=0; LastPlayedUtc=''; LastValidatedUtc=''; LastResult='Unknown' }; LaunchBoxLink=$null " ^
  "}; " ^
  "$reg.Games += $entry; " ^
  "$reg | ConvertTo-Json -Depth 12 | Set-Content 'Registry.json' -Encoding utf8"

echo Launching notepad with overlay...
Relay.exe launch --key %TEST_KEY% --overlay true
set "EXIT_CODE=%ERRORLEVEL%"
echo Exit code: %EXIT_CODE%

for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  echo Latest log: Logs\%%F
  powershell -NoProfile -Command "Get-Content 'Logs\%%F' -Tail 120"
  goto :done
)
echo No relay log file found.

:done
popd
pause
exit /b %EXIT_CODE%
