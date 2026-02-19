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

set "ROOT_A=%PUBLISH_DIR%\GamesA"
set "ROOT_B=%PUBLISH_DIR%\GamesB"
set "KEY_A=44444444-4444-4444-4444-444444444444"
set "KEY_B=55555555-5555-5555-5555-555555555555"

pushd "%PUBLISH_DIR%"

powershell -NoProfile -Command ^
  "$ErrorActionPreference='Stop'; " ^
  "$identity='target={GamesRoot}\Relay.exe|args=validate|workdir={GamesRoot}'; " ^
  "$sha=New-Object System.Security.Cryptography.SHA256Managed; " ^
  "$hash=$sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($identity)); " ^
  "$launchKey=([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant(); " ^
  "New-Item -Path '%ROOT_A%' -ItemType Directory -Force | Out-Null; " ^
  "New-Item -Path '%ROOT_B%' -ItemType Directory -Force | Out-Null; " ^
  "$cfg=[pscustomobject]@{ " ^
  "  SchemaVersion=1; " ^
  "  LaunchBox=[pscustomobject]@{ RootPath=''; PreferredPlatforms=@() }; " ^
  "  Paths=[pscustomobject]@{ ShortcutImportRoot='%PUBLISH_DIR%\ShortcutImport'; ShortcutOutputRoot='%PUBLISH_DIR%\ShortcutsOut'; GamesRoot='%ROOT_A%'; CacheRoot='%PUBLISH_DIR%\Cache'; LaunchBoxRoot=''; TempRoot='%PUBLISH_DIR%\Temp'; ScanRoots=@('%PUBLISH_DIR%\ScanRoots') }; " ^
  "  Launch=[pscustomobject]@{ ActuallyLaunch=$true }; " ^
  "  Cache=[pscustomobject]@{ Enabled=$true; MaxBytes=0; PurgePolicy='LRU'; KeepPinned=$true }; " ^
  "  Overlay=[pscustomobject]@{ Enabled=$true; FadeOutMs=300 }; " ^
  "  Scanning=[pscustomobject]@{ Enabled=$true; MinExeBytes=524288; ExcludedExePatterns=@(); ExcludedFolderNames=@() }; " ^
  "  ScannerRules=[pscustomobject]@{ ExeIgnoreNamePatterns=@(); ExeIgnoreFolderNamePatterns=@() }; " ^
  "  Diagnostics=[pscustomobject]@{ VerboseLogging=$false } " ^
  "}; " ^
  "$cfg | ConvertTo-Json -Depth 12 | Set-Content 'Config.json' -Encoding utf8; " ^
  "$registry=[pscustomobject]@{ SchemaVersion=1; HiddenExecutables=@(); Games=@() }; " ^
  "$entryA=[pscustomobject]@{ " ^
  "  GameKey='%KEY_A%'; LaunchKey=$launchKey; ImportedFromShortcut=$false; ImportedShortcutIconLocation=''; DisplayName='LaunchKey Stable A'; " ^
  "  Install=[pscustomobject]@{ GameFolderPath='%ROOT_A%\SameGame'; BaseFolder='%ROOT_A%\SameGame'; ExePath='%ROOT_A%\Relay.exe'; ToolExePaths=@(); Args='validate'; WorkingDir='%ROOT_A%' }; " ^
  "  Launch=[pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=[pscustomobject]@{ TargetPath='{GamesRoot}\Relay.exe'; Arguments='validate'; WorkingDirectory='{GamesRoot}' }; Tools=@{} }; " ^
  "  Stats=[pscustomobject]@{ EstimatedBytes=0; LastPlayedUtc=''; LastValidatedUtc=''; LastResult='Unknown' }; LaunchBoxLink=$null " ^
  "}; " ^
  "$cfg.Paths.GamesRoot='%ROOT_B%'; " ^
  "$cfg | ConvertTo-Json -Depth 12 | Set-Content 'Config.json' -Encoding utf8; " ^
  "$entryB=[pscustomobject]@{ " ^
  "  GameKey='%KEY_B%'; LaunchKey=$launchKey; ImportedFromShortcut=$false; ImportedShortcutIconLocation=''; DisplayName='LaunchKey Stable B'; " ^
  "  Install=[pscustomobject]@{ GameFolderPath='%ROOT_B%\SameGame'; BaseFolder='%ROOT_B%\SameGame'; ExePath='%ROOT_B%\Relay.exe'; ToolExePaths=@(); Args='validate'; WorkingDir='%ROOT_B%' }; " ^
  "  Launch=[pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=[pscustomobject]@{ TargetPath='{GamesRoot}\Relay.exe'; Arguments='validate'; WorkingDirectory='{GamesRoot}' }; Tools=@{} }; " ^
  "  Stats=[pscustomobject]@{ EstimatedBytes=0; LastPlayedUtc=''; LastValidatedUtc=''; LastResult='Unknown' }; LaunchBoxLink=$null " ^
  "}; " ^
  "$registry.Games += $entryA; " ^
  "$registry.Games += $entryB; " ^
  "$registry | ConvertTo-Json -Depth 12 | Set-Content 'Registry.json' -Encoding utf8; " ^
  "Write-Host ('LaunchKey A: ' + $entryA.LaunchKey); " ^
  "Write-Host ('LaunchKey B: ' + $entryB.LaunchKey); " ^
  "if ($entryA.LaunchKey -eq $entryB.LaunchKey) { Write-Host 'PASS: LaunchKey remained stable across root changes.'; exit 0 } else { Write-Host 'FAIL: LaunchKey changed.'; exit 2 }"

set "EXIT_CODE=%ERRORLEVEL%"

Relay.exe validate
echo Validate exit code: %ERRORLEVEL%
echo Script exit code: %EXIT_CODE%

popd
pause
exit /b %EXIT_CODE%
