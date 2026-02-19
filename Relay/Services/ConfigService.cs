using System.IO;
using System.Text.Json;
using Relay.Data;

namespace Relay.Services;

public sealed class ConfigService(LoggingService logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private string ConfigPath => Path.Combine(BaseDirectory, "Config.json");

    public async Task EnsureExistsAsync()
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        var model = new Config();
        EnsureDefaultFolders(model, out _);
        await SaveAsync(model);
        logger.Info("Created default Config.json");
    }

    public async Task<Config> LoadAsync()
    {
        await EnsureExistsAsync();
        var json = await File.ReadAllTextAsync(ConfigPath);
        var config = JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();

        EnsureDefaultFolders(config, out var changed);
        if (changed)
        {
            await SaveAsync(config);
        }

        return config;
    }

    public async Task SaveAsync(Config config)
    {
        await using var stream = File.Create(ConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    private static void EnsureDefaultFolders(Config config, out bool changed)
    {
        changed = false;

        if (string.IsNullOrWhiteSpace(config.Paths.ShortcutImportRoot))
        {
            config.Paths.ShortcutImportRoot = ".\\ShortcutImport";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.ShortcutOutputRoot))
        {
            config.Paths.ShortcutOutputRoot = ".\\ShortcutsOut";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.CacheRoot))
        {
            config.Paths.CacheRoot = ".\\Cache";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.GamesRoot))
        {
            var firstScanRoot = config.Paths.ScanRoots?.FirstOrDefault() ?? string.Empty;
            config.Paths.GamesRoot = string.IsNullOrWhiteSpace(firstScanRoot)
                ? ".\\ScanRoots"
                : firstScanRoot;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.LaunchBoxRoot))
        {
            config.Paths.LaunchBoxRoot = config.LaunchBox.RootPath ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.TempRoot))
        {
            config.Paths.TempRoot = ".\\Temp";
            changed = true;
        }

        if (config.Paths.ScanRoots is null)
        {
            config.Paths.ScanRoots = [];
            changed = true;
        }

        if (config.Paths.ScanRoots.Count == 0)
        {
            config.Paths.ScanRoots.Add(".\\ScanRoots");
            changed = true;
        }
        else if (string.IsNullOrWhiteSpace(config.Paths.ScanRoots[0]))
        {
            config.Paths.ScanRoots[0] = ".\\ScanRoots";
            changed = true;
        }

        var importRoot = ResolvePath(config.Paths.ShortcutImportRoot);
        var outputRoot = ResolvePath(config.Paths.ShortcutOutputRoot);
        var gamesRoot = ResolvePath(config.Paths.GamesRoot);
        var cacheRoot = ResolvePath(config.Paths.CacheRoot);
        var launchBoxRoot = ResolvePath(config.Paths.LaunchBoxRoot);
        var tempRoot = ResolvePath(config.Paths.TempRoot);
        var scanRoot = ResolvePath(config.Paths.ScanRoots[0]);

        if (!string.Equals(importRoot, config.Paths.ShortcutImportRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.ShortcutImportRoot = importRoot;
            changed = true;
        }

        if (!string.Equals(outputRoot, config.Paths.ShortcutOutputRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.ShortcutOutputRoot = outputRoot;
            changed = true;
        }

        if (!string.Equals(gamesRoot, config.Paths.GamesRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.GamesRoot = gamesRoot;
            changed = true;
        }

        if (!string.Equals(cacheRoot, config.Paths.CacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.CacheRoot = cacheRoot;
            changed = true;
        }

        if (!string.Equals(launchBoxRoot, config.Paths.LaunchBoxRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.LaunchBoxRoot = launchBoxRoot;
            changed = true;
        }

        if (!string.Equals(tempRoot, config.Paths.TempRoot, StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.TempRoot = tempRoot;
            changed = true;
        }

        if (!string.Equals(scanRoot, config.Paths.ScanRoots[0], StringComparison.OrdinalIgnoreCase))
        {
            config.Paths.ScanRoots[0] = scanRoot;
            changed = true;
        }

        config.ScannerRules ??= new ScannerRulesConfig();
        config.ScannerRules.ExeIgnoreNamePatterns ??= [];
        config.ScannerRules.ExeIgnoreFolderNamePatterns ??= [];

        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "vc_redist*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "vcredist*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "language.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "language_*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "*language*selector*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "unins*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "setup*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "install*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "updater*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "crashreport*.exe");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreNamePatterns, "dxsetup.exe");

        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*redist*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*_commonredist*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*installer*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*install*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*uninstall*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*support*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*directx*");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "vc");
        changed |= EnsureContains(config.ScannerRules.ExeIgnoreFolderNamePatterns, "*vcredist*");

        config.Overlay ??= new OverlayConfig();
        if (config.Overlay.AutoCloseSeconds <= 0)
        {
            config.Overlay.AutoCloseSeconds = 3;
            changed = true;
        }

        if (config.Overlay.FadeOutMs <= 0)
        {
            config.Overlay.FadeOutMs = 300;
            changed = true;
        }

        Directory.CreateDirectory(importRoot);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(scanRoot);
    }

    private static bool EnsureContains(List<string> values, string expected)
    {
        if (values.Any(v => string.Equals(v, expected, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        values.Add(expected);
        return true;
    }

    private static string ResolvePath(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(pathOrRelative))
        {
            return Path.GetFullPath(pathOrRelative);
        }

        return Path.GetFullPath(Path.Combine(BaseDirectory, pathOrRelative));
    }
}
