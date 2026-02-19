# Relay

Relay is a standalone launcher and registry manager for DRM-free games used alongside LaunchBox.

## Modes

- Manager mode: run with no arguments to open the WPF manager UI.

## CLI

- `relay launch --key <guid>`
- `relay launch --key <guid> --overlay true|false`
- `relay manager`
- `relay validate`

## Data Files

`Config.json` and `Registry.json` are resolved from `AppContext.BaseDirectory` at runtime.

## Path Tokens And Roots

Relay supports these tokens in launch contracts:

- `{GamesRoot}` from `Config.json -> Paths.GamesRoot`
- `{CacheRoot}` from `Config.json -> Paths.CacheRoot`
- `{LaunchBoxRoot}` from `Config.json -> Paths.LaunchBoxRoot`
- `{GameFolder}` from game install metadata
- `{MainExeDir}` resolved from the selected target path
- `{RelayDir}` from the Relay runtime directory

Root tokens are preferred when tokenizing absolute paths for persistence in session state and registry launch contracts.

## Overlay

CLI launch can show a non-modal top-right overlay with launch stages and preflight details.

Config fields:

- `Overlay.Enabled` (default `true`)
- `Overlay.AutoCloseSeconds` (default `3`)
- `Overlay.ShowDetails` (default `true`)
- `Overlay.FadeOutMs` (kept for compatibility)

## How to publish

Run from project folder:

`dotnet publish -c Release -r win-x64`

## Testing

Use the scripts in `scripts\` (double-click friendly; all scripts pause at the end):

- `scripts\clean_and_publish.bat` - cleans Release artifacts, deletes old publish output, republishes.
- `scripts\test_validate.bat` - runs `Relay.exe validate`, shows exit code, tails latest log.
- `scripts\test_manager.bat` - runs `Relay.exe manager`, shows exit code after close, tails latest log.
- `scripts\test_launch_fakekey.bat` - runs launch with fake GUID, shows exit code (expected `10`), tails latest log.
- `scripts\test_import_shortcuts.bat` - runs validate then opens manager for UI-driven shortcut import testing.
- `scripts\test_scan_folders_metrics.bat` - runs validate and prints Config path for folder-scan/metrics UI testing.
- `scripts\test_launch_key.bat` - prompts for GUID (or uses `RELAY_KEY`) and runs `launch --key`.
- `scripts\test_validate_and_tail_log.bat` - runs validate and tails latest relay log.
- `scripts\reset_runtime_files.bat` - removes `Config.json`, `Registry.json`, and `Logs`, then runs validate.
- `scripts\test_roots_tokenize_and_launch.bat` - configures root paths, writes a `{GamesRoot}` launch contract, launches by key, and verifies resolved-target success log.
- `scripts\test_launchkey_stability.bat` - changes configured roots across two entries with identical tokenized contracts and verifies LaunchKey stability.
- `scripts\smoke_test_overlay_notepad.bat` - launches notepad via `{GamesRoot}` with overlay enabled and tails log.
- `scripts\test_canonicalization_edgecases.bat` - runs messy path/args launch contract twice and confirms LaunchKey stability.
