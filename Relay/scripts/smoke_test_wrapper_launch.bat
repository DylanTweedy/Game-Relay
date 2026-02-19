@echo off
setlocal EnableDelayedExpansion

set "ROOT=D:\LaunchBox\Apps\Relay"
set "PUBLISH=%ROOT%\bin\Release\net8.0-windows\win-x64\publish"
set "GUID=11111111-1111-1111-1111-111111111111"

if not exist "%PUBLISH%\Relay.exe" (
  echo Relay.exe not found at: %PUBLISH%
  echo Run scripts\clean_and_publish.bat first.
  pause
  exit /b 1
)

cd /d "%PUBLISH%"
echo [1/4] Validate runtime files...
Relay.exe validate
echo validate exit code: %ERRORLEVEL%

echo [2/4] Ensure smoke test game entry exists in Registry.json...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$registryPath = Join-Path $PWD 'Registry.json'; if (-not (Test-Path $registryPath)) { throw 'Registry.json not found.' }; $json = Get-Content $registryPath -Raw | ConvertFrom-Json; if ($null -eq $json.Games) { $json | Add-Member -NotePropertyName Games -NotePropertyValue @() -Force }; $guid = '%GUID%'; $existing = $json.Games | Where-Object { $_.GameKey -eq $guid } | Select-Object -First 1; if (-not $existing) { $entry = [pscustomobject]@{ GameKey = $guid; DisplayName = 'Smoke Notepad'; Install = [pscustomobject]@{ GameFolderPath = $env:WINDIR; ExePath = (Join-Path $env:WINDIR 'System32\notepad.exe'); ToolExePaths = @(); Args = ''; WorkingDir = (Join-Path $env:WINDIR 'System32') }; Launch = [pscustomobject]@{ PreferredMode = 'Cache'; AllowOverlay = $true; Pinned = $false; Main = [pscustomobject]@{ TargetPath = (Join-Path $env:WINDIR 'System32\notepad.exe'); Arguments = ''; WorkingDirectory = (Join-Path $env:WINDIR 'System32') }; Tools = @{} }; Stats = [pscustomobject]@{ EstimatedBytes = 0; LastPlayedUtc = ''; LastValidatedUtc = ''; LastResult = 'Unknown' }; LaunchBoxLink = $null }; $json.Games += $entry } else { if (-not $existing.Launch) { $existing | Add-Member -NotePropertyName Launch -NotePropertyValue ([pscustomobject]@{ PreferredMode='Cache'; AllowOverlay=$true; Pinned=$false; Main=$null; Tools=@{} }) -Force }; $existing.Launch.Main = [pscustomobject]@{ TargetPath = (Join-Path $env:WINDIR 'System32\notepad.exe'); Arguments = ''; WorkingDirectory = (Join-Path $env:WINDIR 'System32') }; if (-not $existing.Install) { $existing | Add-Member -NotePropertyName Install -NotePropertyValue ([pscustomobject]@{}) -Force }; $existing.Install.GameFolderPath = $env:WINDIR; $existing.Install.ExePath = (Join-Path $env:WINDIR 'System32\notepad.exe'); $existing.Install.WorkingDir = (Join-Path $env:WINDIR 'System32'); if ($null -eq $existing.Install.ToolExePaths) { $existing.Install | Add-Member -NotePropertyName ToolExePaths -NotePropertyValue @() -Force } }; $json | ConvertTo-Json -Depth 20 | Set-Content -Path $registryPath -Encoding UTF8"
if errorlevel 1 (
  echo Failed to update Registry.json
  pause
  exit /b 1
)

echo [3/4] Launch smoke test game by key...
Relay.exe launch --key %GUID%
set "LAUNCH_EXIT=%ERRORLEVEL%"
echo launch exit code: !LAUNCH_EXIT!

echo [4/4] Tail latest relay log...
set "LATEST="
for /f "delims=" %%F in ('dir /b /o:-d "Logs\relay_*.log" 2^>nul') do (
  set "LATEST=%%F"
  goto :tail
)

echo No relay logs found.
goto :done

:tail
echo Latest log: Logs\!LATEST!
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -Path 'Logs\!LATEST!' -Tail 80"

:done
echo Done.
pause
endlocal
