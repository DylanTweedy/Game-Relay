using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Relay.Core;
using Relay.Data;
using Relay.Data.Models;
using Relay.Services;
using WinForms = System.Windows.Forms;

namespace Relay.UI;

public partial class MainWindow : System.Windows.Window
{
    private readonly LoggingService _logger;
    private readonly ConfigService _configService;
    private readonly RegistryService _registryService;
    private readonly ScanCacheService _scanCacheService;
    private readonly SessionStateService _sessionStateService;
    private readonly Scanner _scanner = new();
    private readonly ShortcutManager _shortcutManager = new();

    private Config _config = new();
    private Registry _registry = new();
    private ScanCache _scanCache = new();

    private readonly ObservableCollection<GameEntry> _games = [];
    private readonly ObservableCollection<GameFolderCandidate> _folderCandidates = [];
    private readonly ObservableCollection<ExeCandidate> _selectedFolderExes = [];
    private readonly ObservableCollection<ShortcutScanResult> _shortcutScanResults = [];

    private CancellationTokenSource? _shortcutScanCts;
    private CancellationTokenSource? _folderScanCts;
    private int _expectedFolderCount;

    public MainWindow(LoggingService logger)
    {
        _logger = logger;
        _configService = new ConfigService(_logger);
        _registryService = new RegistryService(_logger);
        _scanCacheService = new ScanCacheService(_logger);
        _sessionStateService = new SessionStateService(_logger);

        InitializeComponent();

        GamesGrid.ItemsSource = _games;
        FolderCandidatesGrid.ItemsSource = _folderCandidates;
        FolderExeGrid.ItemsSource = _selectedFolderExes;
        ShortcutImportGrid.ItemsSource = _shortcutScanResults;
    }

    private async void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await LoadStateAsync();
    }

    private async Task LoadStateAsync()
    {
        await _configService.EnsureExistsAsync();
        await _registryService.EnsureExistsAsync();

        _config = await _configService.LoadAsync();
        _registry = await _registryService.LoadAsync();
        _scanCache = await _scanCacheService.LoadAsync();

        BindConfigToUi();
        var migratedLaunchKeys = RefreshGamesView();
        if (migratedLaunchKeys)
        {
            await _registryService.SaveAsync(_registry);
        }
        ClearMetricsUi();
        await RestoreSessionStateAsync();
    }

    private async Task ReloadStateAsync()
    {
        _config = await _configService.LoadAsync();
        _registry = await _registryService.LoadAsync();
        _scanCache = await _scanCacheService.LoadAsync();
        BindConfigToUi();
    }

    private void BindConfigToUi()
    {
        var firstRoot = _config.Paths.ScanRoots?.FirstOrDefault() ?? string.Empty;
        ScanRootTextBox.Text = firstRoot;
        ShortcutOutputRootTextBox.Text = _config.Paths.ShortcutOutputRoot ?? string.Empty;
        ShortcutImportRootTextBox.Text = _config.Paths.ShortcutImportRoot ?? string.Empty;
        GamesRootTextBox.Text = _config.Paths.GamesRoot ?? string.Empty;
        CacheRootTextBox.Text = _config.Paths.CacheRoot ?? string.Empty;
        LaunchBoxRootTextBox.Text = _config.Paths.LaunchBoxRoot ?? string.Empty;
    }

    private async Task SaveConfigAsync()
    {
        await _configService.SaveAsync(_config);
    }

    private async Task RestoreSessionStateAsync()
    {
        try
        {
            var state = await _sessionStateService.LoadAsync();
            _shortcutScanResults.Clear();
            foreach (var row in state.ShortcutResults)
            {
                _shortcutScanResults.Add(row);
            }

            _folderCandidates.Clear();
            foreach (var folder in state.FolderCandidates)
            {
                folder.SelectedToolExePaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                folder.Exes ??= [];
                foreach (var exe in folder.Exes)
                {
                    if (folder.SelectedToolExePaths.Contains(exe.ExePath))
                    {
                        exe.IsToolSelected = true;
                    }
                }

                _folderCandidates.Add(folder);
            }

            foreach (var pair in state.LaunchMainByGameKey)
            {
                if (!Guid.TryParse(pair.Key, out var key))
                {
                    continue;
                }

                var game = _registry.Games.FirstOrDefault(g => g.GameKey == key);
                if (game is null)
                {
                    continue;
                }

                game.Launch.Main = pair.Value ?? new LaunchContract();
            }

            _ = RefreshGamesView();

            if (_folderCandidates.Count > 0)
            {
                FolderCandidatesGrid.SelectedIndex = 0;
                BindFolderExes(_folderCandidates[0]);
                RecomputeMetrics();
            }
            else
            {
                _selectedFolderExes.Clear();
                ClearMetricsUi();
            }

            if (!string.IsNullOrWhiteSpace(state.ShortcutsStatusText))
            {
                ShortcutsStatusText.Text = state.ShortcutsStatusText;
            }

            if (!string.IsNullOrWhiteSpace(state.ScannerStatusText))
            {
                ScannerStatusText.Text = state.ScannerStatusText;
            }

            _logger.Info($"Session state restored: shortcuts={_shortcutScanResults.Count}, folders={_folderCandidates.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Session Restore Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private SessionState CreateSessionSnapshot()
    {
        return new SessionState
        {
            SavedUtc = DateTime.UtcNow.ToString("O"),
            ShortcutResults = _shortcutScanResults.ToList(),
            FolderCandidates = _folderCandidates.ToList(),
            LaunchMainByGameKey = _registry.Games.ToDictionary(
                g => g.GameKey.ToString(),
                g => g.Launch.Main ?? new LaunchContract(),
                StringComparer.OrdinalIgnoreCase),
            ScannerStatusText = ScannerStatusText.Text ?? string.Empty,
            ShortcutsStatusText = ShortcutsStatusText.Text ?? string.Empty
        };
    }

    private void RequestSessionSave(string reason)
    {
        try
        {
            _sessionStateService.RequestSave(CreateSessionSnapshot(), reason);
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
        }
    }

    private async Task SaveSessionStateNowAsync(string reason, bool explicitUserAction, CancellationToken ct = default)
    {
        try
        {
            await _sessionStateService.SaveNowAsync(CreateSessionSnapshot(), reason, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            if (!explicitUserAction)
            {
                return;
            }

            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Session Save Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private bool RefreshGamesView()
    {
        var changed = false;
        _games.Clear();
        foreach (var game in _registry.Games.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            game.Launch.Main ??= new LaunchContract();
            game.Launch.Tools ??= new(StringComparer.OrdinalIgnoreCase);
            game.Install.ToolExePaths ??= [];

            if (string.IsNullOrWhiteSpace(game.LaunchKey))
            {
                game.Launch.Main.TargetPath = PathTokenizer.TokenizeForStorage(game.Launch.Main.TargetPath, game.Install, _config, AppContext.BaseDirectory);
                game.Launch.Main.WorkingDirectory = PathTokenizer.TokenizeForStorage(game.Launch.Main.WorkingDirectory, game.Install, _config, AppContext.BaseDirectory);
                foreach (var toolContract in game.Launch.Tools.Values)
                {
                    toolContract.TargetPath = PathTokenizer.TokenizeForStorage(toolContract.TargetPath, game.Install, _config, AppContext.BaseDirectory);
                    toolContract.WorkingDirectory = PathTokenizer.TokenizeForStorage(toolContract.WorkingDirectory, game.Install, _config, AppContext.BaseDirectory);
                }

                game.LaunchKey = LaunchIdentity.BuildLaunchKeyForGame(game, _config, AppContext.BaseDirectory);
                changed = true;
            }

            _games.Add(game);
        }

        return changed;
    }

    private void ClearMetricsUi()
    {
        UpdateMetricsUi(new ScanMetrics());
    }

    private async void BrowseShortcutImportRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(ShortcutImportRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _config.Paths.ShortcutImportRoot = selected;
        ShortcutImportRootTextBox.Text = selected;
        await SaveConfigAsync();
    }

    private async void ScanShortcuts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await ReloadStateAsync();

        var importFolder = _config.Paths.ShortcutImportRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(importFolder))
        {
            ShortcutsStatusText.Text = "ShortcutImportRoot is empty. Choose an import folder first.";
            return;
        }

        _logger.Info($"Shortcut scan start: root={importFolder}");

        _shortcutScanCts?.Cancel();
        _shortcutScanCts?.Dispose();
        _shortcutScanCts = new CancellationTokenSource();

        ShortcutScanProgressBar.Visibility = System.Windows.Visibility.Visible;
        ShortcutScanProgressBar.IsIndeterminate = true;
        ShortcutScanProgressBar.Value = 0;
        _shortcutScanResults.Clear();
        ShortcutsStatusText.Text = "Scanning shortcuts... 0 found";

        var relayExePath = Path.Combine(AppContext.BaseDirectory, "Relay.exe");
        var onItem = new Progress<ShortcutScanResult>(item =>
        {
            _shortcutScanResults.Add(item);
            ShortcutsStatusText.Text = $"Scanning shortcuts... {_shortcutScanResults.Count} found";
            if (_shortcutScanResults.Count % 20 == 0)
            {
                RequestSessionSave("ShortcutScanStreaming");
            }
        });

        var onProgress = new Progress<ScanProgress>(progress =>
        {
            if (progress.Total > 0)
            {
                ShortcutScanProgressBar.IsIndeterminate = false;
                ShortcutScanProgressBar.Maximum = progress.Total;
                ShortcutScanProgressBar.Value = progress.Processed;
            }
            else
            {
                ShortcutScanProgressBar.IsIndeterminate = true;
            }
        });

        try
        {
            await _shortcutManager.ScanShortcutsAsync(
                importFolder,
                relayExePath,
                _registry,
                onItem,
                onProgress,
                _shortcutScanCts.Token);

            ShortcutsStatusText.Text = $"Scan complete. {_shortcutScanResults.Count} shortcut(s) loaded.";
            _logger.Info($"Shortcut scan completed: count={_shortcutScanResults.Count}");
            await SaveSessionStateNowAsync("ShortcutScanCompleted", explicitUserAction: false);
        }
        catch (OperationCanceledException)
        {
            ShortcutsStatusText.Text = $"Scan cancelled. {_shortcutScanResults.Count} shortcut(s) found.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Shortcut scan failed: {ex}");
            ShortcutsStatusText.Text = $"Shortcut scan failed: {ex.Message}";
        }
        finally
        {
            ShortcutScanProgressBar.IsIndeterminate = false;
            ShortcutScanProgressBar.Visibility = System.Windows.Visibility.Collapsed;
            _shortcutScanCts?.Dispose();
            _shortcutScanCts = null;
        }
    }

    private void CancelShortcutScan_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _shortcutScanCts?.Cancel();
    }

    private void ShortcutImportGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ShortcutImportGrid.SelectedItem is not ShortcutScanResult row)
        {
            ShortcutLaunchTargetTextBox.Text = string.Empty;
            ShortcutLaunchArgsTextBox.Text = string.Empty;
            ShortcutLaunchWorkingDirTextBox.Text = string.Empty;
            return;
        }

        ShortcutLaunchTargetTextBox.Text = row.TargetPath;
        ShortcutLaunchArgsTextBox.Text = row.Arguments;
        ShortcutLaunchWorkingDirTextBox.Text = row.WorkingDirectory;
    }

    private void BrowseShortcutLaunchTarget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = ShortcutLaunchTargetTextBox.Text
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ShortcutLaunchTargetTextBox.Text = dialog.FileName;
        }
    }

    private void ApplyShortcutEditorToSelectedRow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ShortcutImportGrid.SelectedItem is not ShortcutScanResult row)
        {
            return;
        }

        row.TargetPath = ShortcutLaunchTargetTextBox.Text?.Trim() ?? string.Empty;
        row.Arguments = ShortcutLaunchArgsTextBox.Text ?? string.Empty;
        row.WorkingDirectory = ShortcutLaunchWorkingDirTextBox.Text?.Trim() ?? string.Empty;
        row.TargetExists = File.Exists(row.TargetPath);
        ShortcutImportGrid.Items.Refresh();
        RequestSessionSave("EditShortcutRow");
    }

    private async void AddSelectedShortcutToRegistry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedItems = ShortcutImportGrid.SelectedItems.OfType<ShortcutScanResult>().ToList();
        if (selectedItems.Count == 0)
        {
            ShortcutsStatusText.Text = "Select one or more shortcut rows first.";
            return;
        }

        await AddShortcutResultsToRegistryAsync(selectedItems);
    }

    private async void AddAllValidShortcuts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var includeOther = IncludeOtherValidCheckBox.IsChecked == true;
        var valid = _shortcutScanResults
            .Where(r => ShortcutManager.IsImportable(r, includeOther))
            .ToList();

        await AddShortcutResultsToRegistryAsync(valid);
    }

    private async Task AddShortcutResultsToRegistryAsync(List<ShortcutScanResult> items)
    {
        var includeOther = IncludeOtherValidCheckBox.IsChecked == true;
        var added = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            if (_shortcutManager.AddDirectShortcutToRegistry(item, _config, _registry, _folderCandidates, _config.Paths.ScanRoots, includeOther))
            {
                item.Status = "AlreadyInRegistry";
                added++;
            }
            else
            {
                skipped++;
            }
        }

        if (added > 0)
        {
            await _registryService.SaveAsync(_registry);
            _ = RefreshGamesView();
        }

        ShortcutImportGrid.Items.Refresh();
        ShortcutsStatusText.Text = $"Added {added}, Skipped {skipped}.";
        await SaveSessionStateNowAsync("ShortcutResultsAddedToRegistry", explicitUserAction: true);
    }

    private async void BrowseScanRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(ScanRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _config.Paths.ScanRoots = [selected];
        ScanRootTextBox.Text = selected;
        await SaveConfigAsync();
    }

    private async void ScanNow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RunFolderScanAsync(ignoreCache: ForceFullRescanCheckBox.IsChecked == true, rescanKnown: false);
    }

    private async void RescanAllIgnoreCache_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RunFolderScanAsync(ignoreCache: true, rescanKnown: true);
    }

    private async void RescanKnown_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RunFolderScanAsync(ignoreCache: true, rescanKnown: true);
    }

    private async Task RunFolderScanAsync(bool ignoreCache, bool rescanKnown)
    {
        await ReloadStateAsync();

        var selectedRoot = ScanRootTextBox.Text?.Trim() ?? string.Empty;
        var roots = !string.IsNullOrWhiteSpace(selectedRoot)
            ? new List<string> { selectedRoot }
            : (_config.Paths.ScanRoots ?? []);

        _logger.Info($"Folder scan start: roots={string.Join(";", roots)} ignoreCache={ignoreCache} rescanKnown={rescanKnown}");

        _folderScanCts?.Cancel();
        _folderScanCts?.Dispose();
        _folderScanCts = new CancellationTokenSource();

        _folderCandidates.Clear();
        _selectedFolderExes.Clear();

        _expectedFolderCount = CountTopLevelFolders(roots);
        ScannerProgressBar.Visibility = System.Windows.Visibility.Visible;
        ScannerProgressBar.IsIndeterminate = _expectedFolderCount == 0;
        ScannerProgressBar.Value = 0;
        ScannerStatusText.Text = "Scanning folders...";

        var incremental = IncrementalScanCheckBox.IsChecked == true;
        var skipKnown = !rescanKnown && SkipKnownFoldersCheckBox.IsChecked == true;
        var forceFull = ignoreCache || ForceFullRescanCheckBox.IsChecked == true;

        var onFolder = new Progress<GameFolderCandidate>(folder =>
        {
            _folderCandidates.Add(folder);
            if (FolderCandidatesGrid.SelectedItem is null)
            {
                FolderCandidatesGrid.SelectedItem = folder;
            }

            var pct = _expectedFolderCount > 0
                ? (int)Math.Round((_folderCandidates.Count / (double)_expectedFolderCount) * 100)
                : 0;

            ScannerStatusText.Text = $"Scanning folders... {_folderCandidates.Count}/{Math.Max(_expectedFolderCount, _folderCandidates.Count)} ({pct}%) last={folder.Source}";
            if (_folderCandidates.Count % 25 == 0)
            {
                RequestSessionSave("FolderScanStreaming");
            }
        });

        var onMetrics = new Progress<ScanMetrics>(metrics =>
        {
            UpdateMetricsUi(metrics);
            if (_expectedFolderCount > 0)
            {
                ScannerProgressBar.IsIndeterminate = false;
                ScannerProgressBar.Maximum = _expectedFolderCount;
                ScannerProgressBar.Value = Math.Min(metrics.TotalFolders, _expectedFolderCount);
            }
        });

        try
        {
            await _scanner.ScanGameFoldersAsync(
                roots,
                _registry,
                _config,
                _scanCache,
                incremental,
                skipKnown,
                forceFull,
                onFolder,
                onMetrics,
                _folderScanCts.Token);

            await _scanCacheService.SaveAsync(_scanCache);
            await _registryService.SaveAsync(_registry);
            _ = RefreshGamesView();

            ScannerStatusText.Text = $"Scan complete. {_folderCandidates.Count} folder(s) loaded.";
            RecomputeMetrics();
            _logger.Info($"Folder scan completed: folders={_folderCandidates.Count}");
            await SaveSessionStateNowAsync("FolderScanCompleted", explicitUserAction: false);
        }
        catch (OperationCanceledException)
        {
            ScannerStatusText.Text = $"Scan cancelled. {_folderCandidates.Count} folder(s) loaded.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Folder scan failed: {ex}");
            ScannerStatusText.Text = $"Folder scan failed: {ex.Message}";
        }
        finally
        {
            ScannerProgressBar.Visibility = System.Windows.Visibility.Collapsed;
            _folderScanCts?.Dispose();
            _folderScanCts = null;
        }
    }

    private void CancelFolderScan_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _folderScanCts?.Cancel();
    }

    private void ClearScanResults_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _folderCandidates.Clear();
        _selectedFolderExes.Clear();
        ClearMetricsUi();
        ScannerStatusText.Text = "Results cleared.";
    }

    private void ClearSessionResults_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _folderCandidates.Clear();
        _selectedFolderExes.Clear();
        _shortcutScanResults.Clear();
        ClearMetricsUi();
        ScannerStatusText.Text = "Session results cleared from UI.";
        ShortcutsStatusText.Text = "Session results cleared from UI.";
        RequestSessionSave("ClearSessionResults");
    }

    private async void ClearSessionFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await _sessionStateService.DeleteAsync();
            var sessionPath = _sessionStateService.GetSessionPath();
            ScannerStatusText.Text = $"Session file deleted: {sessionPath}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Session Delete Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async void ClearScanCache_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await _scanCacheService.ClearAsync();
        _scanCache = new ScanCache();
        ScannerStatusText.Text = "Scan cache cleared.";
    }

    private void FolderCandidatesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FolderCandidatesGrid.SelectedItem is not GameFolderCandidate folder)
        {
            _selectedFolderExes.Clear();
            return;
        }

        ScannerStatusText.Text = $"Selected folder source: {folder.Source}, state: {folder.State}";
        BindFolderExes(folder);
    }

    private async void AddAllMainToRegistry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var added = 0;
        var already = 0;
        var missing = 0;
        var invalid = 0;
        foreach (var folder in _folderCandidates)
        {
            var classification = ClassifyMainAddSkip(folder);
            if (classification == "Missing")
            {
                missing++;
                continue;
            }

            if (classification == "Invalid")
            {
                invalid++;
                continue;
            }

            if (classification == "AlreadyInRegistry")
            {
                already++;
                continue;
            }

            if (_scanner.AddMainForFolderToRegistry(folder, _registry, _config))
            {
                added++;
            }
        }

        var skipped = already + missing + invalid;
        if (added > 0)
        {
            await _registryService.SaveAsync(_registry);
            _ = RefreshGamesView();
            await SaveSessionStateNowAsync("AddAllMainToRegistry", explicitUserAction: true);
        }

        ScannerStatusText.Text = $"Added {added}, Skipped {skipped} (Already in Registry {already}, Missing {missing}, Invalid {invalid}).";
    }

    private async void AddSelectedFoldersMainToRegistry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedFolders = FolderCandidatesGrid.SelectedItems.OfType<GameFolderCandidate>().ToList();
        if (selectedFolders.Count == 0)
        {
            ScannerStatusText.Text = "Select one or more folders first.";
            return;
        }

        var added = 0;
        var already = 0;
        var missing = 0;
        var invalid = 0;
        foreach (var folder in selectedFolders)
        {
            var classification = ClassifyMainAddSkip(folder);
            if (classification == "Missing")
            {
                missing++;
                continue;
            }

            if (classification == "Invalid")
            {
                invalid++;
                continue;
            }

            if (classification == "AlreadyInRegistry")
            {
                already++;
                continue;
            }

            if (_scanner.AddMainForFolderToRegistry(folder, _registry, _config))
            {
                added++;
            }
        }

        var skipped = already + missing + invalid;
        if (added > 0)
        {
            await _registryService.SaveAsync(_registry);
            _ = RefreshGamesView();
            await SaveSessionStateNowAsync("AddSelectedMainToRegistry", explicitUserAction: true);
        }

        ScannerStatusText.Text = $"Added {added}, Skipped {skipped} (Already in Registry {already}, Missing {missing}, Invalid {invalid}).";
    }

    private async void AddSelectedFoldersToolsToRegistry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedFolders = FolderCandidatesGrid.SelectedItems.OfType<GameFolderCandidate>().ToList();
        if (selectedFolders.Count == 0)
        {
            ScannerStatusText.Text = "Select one or more folders first.";
            return;
        }

        var updated = 0;
        var already = 0;
        var missing = 0;
        var invalid = 0;
        foreach (var folder in selectedFolders)
        {
            if (folder.SelectedToolExePaths.Count == 0)
            {
                missing++;
                continue;
            }

            if (folder.SelectedToolExePaths.Any(path => !File.Exists(path)))
            {
                invalid++;
                continue;
            }

            if (_scanner.AddToolsForFolderToRegistry(folder, _registry, _config))
            {
                updated++;
            }
            else
            {
                already++;
            }
        }

        var skipped = already + missing + invalid;
        if (updated > 0)
        {
            await _registryService.SaveAsync(_registry);
            _ = RefreshGamesView();
            await SaveSessionStateNowAsync("AddSelectedToolsToRegistry", explicitUserAction: true);
        }

        ScannerStatusText.Text = $"Added {updated}, Skipped {skipped} (Already in Registry {already}, Missing {missing}, Invalid {invalid}).";
    }

    private void SetMainCandidate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (FolderCandidatesGrid.SelectedItem is not GameFolderCandidate folder)
        {
            return;
        }

        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not ExeCandidate exe)
        {
            return;
        }

        if (_scanner.SetMain(folder, exe.ExePath))
        {
            folder.SelectedToolExePaths.Remove(exe.ExePath);
            exe.IsToolSelected = false;
            BindFolderExes(folder);
            FolderCandidatesGrid.Items.Refresh();
            RecomputeMetrics();
            RequestSessionSave("SetMainCandidate");
        }
    }

    private void ToolToggle_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (FolderCandidatesGrid.SelectedItem is not GameFolderCandidate folder)
        {
            return;
        }

        if ((sender as System.Windows.Controls.CheckBox)?.DataContext is not ExeCandidate exe)
        {
            return;
        }

        var selected = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;

        if (!string.IsNullOrWhiteSpace(folder.SelectedMainExePath) &&
            string.Equals(folder.SelectedMainExePath, exe.ExePath, StringComparison.OrdinalIgnoreCase))
        {
            exe.IsToolSelected = false;
            return;
        }

        _scanner.ToggleTool(folder, exe.ExePath, selected);
        RecomputeMetrics();
        RequestSessionSave("ToggleTool");
    }

    private async void HideFolderExe_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (FolderCandidatesGrid.SelectedItem is not GameFolderCandidate folder)
        {
            return;
        }

        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not ExeCandidate exe)
        {
            return;
        }

        if (!_scanner.HideInRegistry(_registry, exe.ExePath))
        {
            return;
        }

        await _registryService.SaveAsync(_registry);

        exe.Kind = "Hidden";
        exe.Reason = "Path exists in HiddenExecutables";
        exe.IsToolSelected = false;
        folder.SelectedToolExePaths.Remove(exe.ExePath);

        if (!string.IsNullOrWhiteSpace(folder.SelectedMainExePath) &&
            string.Equals(folder.SelectedMainExePath, exe.ExePath, StringComparison.OrdinalIgnoreCase))
        {
            folder.SelectedMainExePath = null;
        }

        folder.HiddenExeCount++;
        folder.ValidExeCount = folder.Exes.Count(e =>
            !string.Equals(e.Kind, "Excluded", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(e.Kind, "Hidden", StringComparison.OrdinalIgnoreCase));

        BindFolderExes(folder);
        FolderCandidatesGrid.Items.Refresh();
        RecomputeMetrics();
        RequestSessionSave("HideFolderExe");
    }

    private void OpenExeFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not ExeCandidate exe)
        {
            return;
        }

        if (!File.Exists(exe.ExePath))
        {
            ScannerStatusText.Text = "File does not exist for Open Folder.";
            return;
        }

        try
        {
            var command = $"explorer.exe /select,\"{exe.ExePath}\"";
            _logger.Info($"Open folder command: {command}");
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{exe.ExePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Open Folder Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void LaunchExeTest_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not ExeCandidate exe)
        {
            return;
        }

        if (!File.Exists(exe.ExePath))
        {
            ScannerStatusText.Text = "Cannot launch test: file missing.";
            return;
        }

        try
        {
            var contract = FindLaunchContractForExe(exe.ExePath);
            var game = FindGameByExe(exe.ExePath);
            var targetRaw = string.IsNullOrWhiteSpace(contract.TargetPath) ? exe.ExePath : contract.TargetPath;
            var target = game is null
                ? targetRaw
                : PathTokenResolver.Resolve(targetRaw, game.Install, contract, _config, AppContext.BaseDirectory);
            var workRaw = !string.IsNullOrWhiteSpace(contract.WorkingDirectory)
                ? contract.WorkingDirectory
                : (Path.GetDirectoryName(target) ?? string.Empty);
            var workdir = game is null
                ? workRaw
                : PathTokenResolver.Resolve(workRaw, game.Install, contract, _config, AppContext.BaseDirectory);
            var args = contract.Arguments ?? string.Empty;

            _logger.Info($"Launch test command: target={target}; args={args}; workdir={workdir}");
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = args,
                WorkingDirectory = workdir,
                UseShellExecute = true
            });

            ScannerStatusText.Text = $"Manual launch test started for {exe.ExeName}.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Launch Test Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async void BrowseShortcutOutputRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(ShortcutOutputRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _config.Paths.ShortcutOutputRoot = selected;
        ShortcutOutputRootTextBox.Text = selected;
        await SaveConfigAsync();
    }

    private async void BrowseGamesRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(GamesRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        GamesRootTextBox.Text = selected;
        await SavePathRootsAsync();
    }

    private async void BrowseCacheRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(CacheRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        CacheRootTextBox.Text = selected;
        await SavePathRootsAsync();
    }

    private async void BrowseLaunchBoxRoot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(PathResolver.Resolve(LaunchBoxRootTextBox.Text));
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        LaunchBoxRootTextBox.Text = selected;
        await SavePathRootsAsync();
    }

    private async void SavePathRoots_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await SavePathRootsAsync();
    }

    private async Task SavePathRootsAsync()
    {
        _config.Paths.GamesRoot = GamesRootTextBox.Text?.Trim() ?? string.Empty;
        _config.Paths.CacheRoot = CacheRootTextBox.Text?.Trim() ?? string.Empty;
        _config.Paths.LaunchBoxRoot = LaunchBoxRootTextBox.Text?.Trim() ?? string.Empty;
        await SaveConfigAsync();
        await ReloadStateAsync();
    }

    private async void GenerateMainWrapperShortcuts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await ReloadStateAsync();
        var result = await _shortcutManager.GenerateMainWrapperShortcutsAsync(_config, _registry);
        ShortcutsStatusText.Text = result.Message;
    }

    private async void GenerateToolsWrapperShortcuts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await ReloadStateAsync();
        var result = await _shortcutManager.GenerateToolsWrapperShortcutsAsync(_config, _registry);
        ShortcutsStatusText.Text = result.Message;
    }

    private async void GenerateDirectShortcuts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await ReloadStateAsync();

        var outputRoot = _config.Paths.ShortcutOutputRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            ShortcutsStatusText.Text = "ShortcutOutputRoot is empty.";
            return;
        }

        var result = await _shortcutManager.GenerateDirectShortcutsAsync(_config, _registry, false);
        ShortcutsStatusText.Text = result.Message;
    }

    private void OpenShortcutFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var outputRoot = _config.Paths.ShortcutOutputRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            ShortcutsStatusText.Text = "ShortcutOutputRoot is empty.";
            return;
        }

        var resolved = PathResolver.Resolve(outputRoot);
        Directory.CreateDirectory(resolved);

        Process.Start(new ProcessStartInfo
        {
            FileName = resolved,
            UseShellExecute = true
        });
    }

    private async void RemoveSelectedGame_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedGames = GamesGrid.SelectedItems.OfType<GameEntry>().ToList();
        if (selectedGames.Count == 0)
        {
            return;
        }

        foreach (var game in selectedGames)
        {
            _registry.Games.Remove(game);
        }

        await _registryService.SaveAsync(_registry);
        _ = RefreshGamesView();
    }

    private async void RefreshGames_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await LoadStateAsync();
    }

    private void GamesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameEntry selected)
        {
            LaunchTargetTextBox.Text = string.Empty;
            LaunchArgumentsTextBox.Text = string.Empty;
            LaunchWorkingDirTextBox.Text = string.Empty;
            GameLaunchValidationText.Text = string.Empty;
            return;
        }

        LaunchTargetTextBox.Text = selected.Launch.Main.TargetPath;
        LaunchArgumentsTextBox.Text = selected.Launch.Main.Arguments;
        LaunchWorkingDirTextBox.Text = selected.Launch.Main.WorkingDirectory;
        UpdateGameLaunchValidationText();
    }

    private void UpdateGameLaunchValidationText()
    {
        var selected = GamesGrid.SelectedItem as GameEntry ?? GamesGrid.SelectedItems.OfType<GameEntry>().FirstOrDefault();
        if (selected is null)
        {
            GameLaunchValidationText.Text = "No game selected.";
            return;
        }

        var contract = new LaunchContract
        {
            TargetPath = LaunchTargetTextBox.Text?.Trim() ?? string.Empty,
            Arguments = LaunchArgumentsTextBox.Text ?? string.Empty,
            WorkingDirectory = LaunchWorkingDirTextBox.Text?.Trim() ?? string.Empty
        };

        if (!PathTokenResolver.TryResolve(contract.TargetPath, selected.Install, contract, _config, AppContext.BaseDirectory, out var resolvedTarget, out var warning))
        {
            GameLaunchValidationText.Text = $"Launch target invalid: {warning}";
            return;
        }

        GameLaunchValidationText.Text = File.Exists(resolvedTarget)
            ? "Launch target exists"
            : "Launch target missing";
    }

    private void BrowseLaunchTarget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = LaunchTargetTextBox.Text
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            LaunchTargetTextBox.Text = dialog.FileName;
            UpdateGameLaunchValidationText();
        }
    }

    private void BrowseLaunchWorkingDir_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = BrowseForFolder(LaunchWorkingDirTextBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            LaunchWorkingDirTextBox.Text = selected;
        }
    }

    private async void SaveLaunchContract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedGames = GamesGrid.SelectedItems.OfType<GameEntry>().ToList();
        if (selectedGames.Count == 0 && GamesGrid.SelectedItem is GameEntry single)
        {
            selectedGames = [single];
        }

        if (selectedGames.Count == 0)
        {
            return;
        }

        foreach (var game in selectedGames)
        {
            var rawTarget = LaunchTargetTextBox.Text?.Trim() ?? string.Empty;
            var rawWorkdir = LaunchWorkingDirTextBox.Text?.Trim() ?? string.Empty;
            var tokenTarget = PathTokenizer.TokenizeForStorage(rawTarget, game.Install, _config, AppContext.BaseDirectory);
            var tokenWorkdir = PathTokenizer.TokenizeForStorage(rawWorkdir, game.Install, _config, AppContext.BaseDirectory);
            var previewContract = new LaunchContract
            {
                TargetPath = tokenTarget,
                Arguments = LaunchArgumentsTextBox.Text ?? string.Empty,
                WorkingDirectory = tokenWorkdir
            };

            if (!PathTokenResolver.TryResolve(previewContract.TargetPath, game.Install, previewContract, _config, AppContext.BaseDirectory, out _, out var warning))
            {
                GameLaunchValidationText.Text = $"Invalid launch target: {warning}";
                return;
            }

            game.Launch.Main = new LaunchContract
            {
                TargetPath = tokenTarget,
                Arguments = LaunchArgumentsTextBox.Text ?? string.Empty,
                WorkingDirectory = tokenWorkdir
            };
            game.LaunchKey = LaunchIdentity.BuildLaunchKeyForGame(game, _config, AppContext.BaseDirectory);
        }

        UpdateGameLaunchValidationText();
        await _registryService.SaveAsync(_registry);
        GamesGrid.Items.Refresh();
        await SaveSessionStateNowAsync("SaveLaunchContract", explicitUserAction: true);
    }

    private async void UseInstallFallbackContract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedGames = GamesGrid.SelectedItems.OfType<GameEntry>().ToList();
        if (selectedGames.Count == 0 && GamesGrid.SelectedItem is GameEntry single)
        {
            selectedGames = [single];
        }

        if (selectedGames.Count == 0)
        {
            return;
        }

        foreach (var game in selectedGames)
        {
            var workdir = !string.IsNullOrWhiteSpace(game.Install.WorkingDir)
                ? game.Install.WorkingDir
                : Path.GetDirectoryName(game.Install.ExePath) ?? game.Install.BaseFolder;

            game.Launch.Main = new LaunchContract
            {
                TargetPath = PathTokenizer.TokenizeForStorage(game.Install.ExePath, game.Install, _config, AppContext.BaseDirectory),
                Arguments = game.Install.Args ?? string.Empty,
                WorkingDirectory = PathTokenizer.TokenizeForStorage(workdir, game.Install, _config, AppContext.BaseDirectory)
            };
            game.LaunchKey = LaunchIdentity.BuildLaunchKeyForGame(game, _config, AppContext.BaseDirectory);
        }

        var first = selectedGames.First();
        LaunchTargetTextBox.Text = first.Launch.Main.TargetPath;
        LaunchArgumentsTextBox.Text = first.Launch.Main.Arguments;
        LaunchWorkingDirTextBox.Text = first.Launch.Main.WorkingDirectory;
        UpdateGameLaunchValidationText();

        await _registryService.SaveAsync(_registry);
        GamesGrid.Items.Refresh();
        await SaveSessionStateNowAsync("ResetToDetectedMain", explicitUserAction: true);
    }

    private async void ApplySelectedShortcutAsLaunchContract_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedGames = GamesGrid.SelectedItems.OfType<GameEntry>().ToList();
        if (selectedGames.Count == 0 && GamesGrid.SelectedItem is GameEntry single)
        {
            selectedGames = [single];
        }

        if (selectedGames.Count == 0)
        {
            ShortcutsStatusText.Text = "Select one or more games in Review & Export.";
            return;
        }

        if (ShortcutImportGrid.SelectedItem is not ShortcutScanResult selectedShortcut)
        {
            ShortcutsStatusText.Text = "Select a shortcut row first in Import Shortcuts.";
            return;
        }

        foreach (var game in selectedGames)
        {
            game.Launch.Main = new LaunchContract
            {
                TargetPath = PathTokenizer.TokenizeForStorage(selectedShortcut.TargetPath, game.Install, _config, AppContext.BaseDirectory),
                Arguments = selectedShortcut.Arguments,
                WorkingDirectory = PathTokenizer.TokenizeForStorage(selectedShortcut.WorkingDirectory, game.Install, _config, AppContext.BaseDirectory)
            };
            game.ImportedFromShortcut = true;
            game.LaunchKey = LaunchIdentity.BuildLaunchKeyForGame(game, _config, AppContext.BaseDirectory);
        }

        await _registryService.SaveAsync(_registry);
        GamesGrid.Items.Refresh();
        ShortcutsStatusText.Text = $"Applied launch command to {selectedGames.Count} game(s).";
        await SaveSessionStateNowAsync("ApplyImportedShortcutCommand", explicitUserAction: true);
    }

    private void PreviewResolvedLaunch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = GamesGrid.SelectedItem as GameEntry ?? GamesGrid.SelectedItems.OfType<GameEntry>().FirstOrDefault();
        if (selected is null)
        {
            System.Windows.MessageBox.Show(
                "Select a game first in Review & Export.",
                "Preview Resolved",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var previewContract = new LaunchContract
        {
            TargetPath = LaunchTargetTextBox.Text?.Trim() ?? string.Empty,
            Arguments = LaunchArgumentsTextBox.Text ?? string.Empty,
            WorkingDirectory = LaunchWorkingDirTextBox.Text?.Trim() ?? string.Empty
        };

        var targetOk = PathTokenResolver.TryResolve(previewContract.TargetPath, selected.Install, previewContract, _config, AppContext.BaseDirectory, out var resolvedTarget, out var targetWarning);
        var workDirOk = PathTokenResolver.TryResolve(previewContract.WorkingDirectory, selected.Install, previewContract, _config, AppContext.BaseDirectory, out var resolvedWorkdir, out var workDirWarning);
        if (string.IsNullOrWhiteSpace(resolvedWorkdir))
        {
            resolvedWorkdir = Path.GetDirectoryName(resolvedTarget) ?? string.Empty;
        }

        var text = $"Target: {resolvedTarget}{Environment.NewLine}" +
                   $"Args: {previewContract.Arguments}{Environment.NewLine}" +
                   $"WorkDir: {resolvedWorkdir}{Environment.NewLine}" +
                   $"Target Valid: {targetOk}{Environment.NewLine}" +
                   $"WorkDir Valid: {workDirOk}";

        if (!string.IsNullOrWhiteSpace(targetWarning))
        {
            text += $"{Environment.NewLine}Target Warning: {targetWarning}";
        }

        if (!string.IsNullOrWhiteSpace(workDirWarning))
        {
            text += $"{Environment.NewLine}WorkDir Warning: {workDirWarning}";
        }

        System.Windows.MessageBox.Show(
            text,
            "Resolved Launch Preview",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static string BrowseForFolder(string initialPath)
    {
        using var dialog = new WinForms.FolderBrowserDialog();

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        var result = dialog.ShowDialog();
        return result == WinForms.DialogResult.OK
            ? dialog.SelectedPath
            : string.Empty;
    }

    private void BindFolderExes(GameFolderCandidate folder)
    {
        _selectedFolderExes.Clear();
        foreach (var exe in folder.Exes)
        {
            exe.IsToolSelected = folder.SelectedToolExePaths.Contains(exe.ExePath);
            _selectedFolderExes.Add(exe);
        }
    }

    private void RecomputeMetrics()
    {
        var metrics = new ScanMetrics
        {
            TotalFolders = _folderCandidates.Count,
            FoldersWithValidExe = _folderCandidates.Count(f => f.ValidExeCount > 0),
            TotalExeCandidates = _folderCandidates.Sum(f => f.Exes.Count),
            MainSelected = _folderCandidates.Count(f => !string.IsNullOrWhiteSpace(f.SelectedMainExePath)),
            ToolsSelected = _folderCandidates.Sum(f => f.SelectedToolExePaths.Count),
            Excluded = _folderCandidates.Sum(f => f.ExcludedExeCount),
            Hidden = _folderCandidates.Sum(f => f.HiddenExeCount),
            CachedFolders = _folderCandidates.Count(f => string.Equals(f.Source, "Cache", StringComparison.OrdinalIgnoreCase)),
            SkippedKnownFolders = _folderCandidates.Count(f => string.Equals(f.State, "Known", StringComparison.OrdinalIgnoreCase))
        };

        UpdateMetricsUi(metrics);
    }

    private void UpdateMetricsUi(ScanMetrics metrics)
    {
        MetricTotalFoldersText.Text = metrics.TotalFolders.ToString();
        MetricFoldersWithValidText.Text = metrics.FoldersWithValidExe.ToString();
        MetricExeCandidatesText.Text = metrics.TotalExeCandidates.ToString();
        MetricMainSelectedText.Text = metrics.MainSelected.ToString();
        MetricToolsSelectedText.Text = metrics.ToolsSelected.ToString();
        MetricExcludedText.Text = metrics.Excluded.ToString();
        MetricHiddenText.Text = metrics.Hidden.ToString();
        MetricCachedText.Text = metrics.CachedFolders.ToString();
        MetricSkippedKnownText.Text = metrics.SkippedKnownFolders.ToString();
    }

    private static int CountTopLevelFolders(IEnumerable<string> roots)
    {
        var count = 0;
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(PathResolver.Resolve).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                count += Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).Count();
            }
            catch
            {
            }
        }

        return count;
    }

    private string ClassifyMainAddSkip(GameFolderCandidate folder)
    {
        var mainPath = folder.SelectedMainExePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mainPath))
        {
            return "Missing";
        }

        if (!File.Exists(mainPath))
        {
            return "Invalid";
        }

        var normalizedMain = NormalizePath(mainPath);
        var normalizedFolder = NormalizePath(folder.FolderPath);
        var tokenTarget = PathTokenizer.TokenizeForStorage(normalizedMain, new InstallInfo
        {
            GameFolderPath = normalizedFolder,
            BaseFolder = normalizedFolder
        }, _config, AppContext.BaseDirectory);
        var tokenWorkDir = PathTokenizer.TokenizeForStorage(Path.GetDirectoryName(normalizedMain) ?? normalizedFolder, new InstallInfo
        {
            GameFolderPath = normalizedFolder,
            BaseFolder = normalizedFolder
        }, _config, AppContext.BaseDirectory);
        var launchKey = LaunchIdentity.BuildLaunchKey(tokenTarget, string.Empty, tokenWorkDir);

        var exists = _registry.Games.Any(g =>
        {
            if (!string.IsNullOrWhiteSpace(g.LaunchKey))
            {
                return LaunchIdentity.IsMatch(g.LaunchKey, launchKey);
            }

            return (!string.IsNullOrWhiteSpace(g.Install.GameFolderPath) && string.Equals(NormalizePath(g.Install.GameFolderPath), normalizedFolder, StringComparison.OrdinalIgnoreCase)) ||
                   (LaunchIdentity.PreferExePathFallback(g.Launch.Main.Arguments) &&
                    !string.IsNullOrWhiteSpace(g.Install.ExePath) &&
                    string.Equals(NormalizePath(g.Install.ExePath), normalizedMain, StringComparison.OrdinalIgnoreCase));
        });

        return exists ? "AlreadyInRegistry" : "Add";
    }

    private LaunchContract FindLaunchContractForExe(string exePath)
    {
        var normalized = NormalizePath(exePath);
        foreach (var game in _registry.Games)
        {
            if (!string.IsNullOrWhiteSpace(game.Install.ExePath) &&
                string.Equals(NormalizePath(game.Install.ExePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return game.Launch.Main ?? new LaunchContract
                {
                    TargetPath = game.Install.ExePath,
                    Arguments = game.Install.Args ?? string.Empty,
                    WorkingDirectory = game.Install.WorkingDir
                };
            }

            if (game.Launch.Tools.TryGetValue(normalized, out var toolContract))
            {
                return toolContract;
            }

            foreach (var pair in game.Launch.Tools)
            {
                if (string.Equals(NormalizePath(pair.Key), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return new LaunchContract
        {
            TargetPath = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
        };
    }

    private GameEntry? FindGameByExe(string exePath)
    {
        var normalized = NormalizePath(exePath);
        return _registry.Games.FirstOrDefault(game =>
            (!string.IsNullOrWhiteSpace(game.Install.ExePath) &&
             string.Equals(NormalizePath(game.Install.ExePath), normalized, StringComparison.OrdinalIgnoreCase)) ||
            game.Install.ToolExePaths.Any(tool => string.Equals(NormalizePath(tool), normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSessionOnClosing();
    }

    private void SaveSessionOnClosing()
    {
        try
        {
            _sessionStateService.CancelPending();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            _sessionStateService.SaveNowAsync(CreateSessionSnapshot(), "WindowClosing", cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private string GetGameLaunchKey(GameEntry game)
    {
        if (!string.IsNullOrWhiteSpace(game.LaunchKey))
        {
            return game.LaunchKey;
        }

        return LaunchIdentity.BuildLaunchKeyForGame(game, _config, AppContext.BaseDirectory);
    }
}
