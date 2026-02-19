using System.IO;
using System.Runtime.InteropServices;
using Relay.Data;
using Relay.Data.Models;

namespace Relay.Core;

public sealed class ShortcutManager
{
    public async Task<ShortcutGenerationResult> GenerateMainWrapperShortcutsAsync(
        Config config,
        Registry registry)
    {
        return await Task.Run(() => GenerateMainWrapperShortcuts(config, registry));
    }

    public async Task<ShortcutGenerationResult> GenerateToolsWrapperShortcutsAsync(
        Config config,
        Registry registry)
    {
        return await Task.Run(() => GenerateToolsWrapperShortcuts(config, registry));
    }

    public async Task<ShortcutGenerationResult> GenerateWrapperShortcutsAsync(
        Config config,
        Registry registry,
        bool cleanOrphans)
    {
        return await Task.Run(() => GenerateShortcuts(config, registry, cleanOrphans, isWrapperMode: true));
    }

    public async Task<ShortcutGenerationResult> GenerateDirectShortcutsAsync(
        Config config,
        Registry registry,
        bool cleanOrphans)
    {
        return await Task.Run(() => GenerateShortcuts(config, registry, cleanOrphans, isWrapperMode: false));
    }

    public async Task<List<ShortcutScanResult>> ScanShortcutsAsync(
        string shortcutFolder,
        string relayExePath,
        Registry registry,
        IProgress<ShortcutScanResult>? onItem,
        IProgress<ScanProgress>? onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() => ScanShortcuts(shortcutFolder, relayExePath, registry, onItem, onProgress, ct), ct);
    }

    public bool AddDirectShortcutToRegistry(
        ShortcutScanResult item,
        Config config,
        Registry registry,
        IEnumerable<GameFolderCandidate>? scannedFolders = null,
        IEnumerable<string>? scanRoots = null,
        bool includeOtherKinds = false)
    {
        if (!IsImportable(item, includeOtherKinds))
        {
            return false;
        }

        var targetPath = NormalizePath(item.TargetPath);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var workingDir = !string.IsNullOrWhiteSpace(item.WorkingDirectory)
            ? NormalizePath(item.WorkingDirectory)
            : Path.GetDirectoryName(targetPath) ?? string.Empty;
        var inferred = InferIdentity(item, scannedFolders, scanRoots);
        var gameFolder = inferred.GameFolderPath;
        var identityExe = inferred.IdentityExePath;
        var isDirectExe = string.Equals(item.Kind, "DirectExe", StringComparison.OrdinalIgnoreCase);
        var install = new InstallInfo
        {
            GameFolderPath = gameFolder,
            BaseFolder = gameFolder,
            ExePath = isDirectExe ? identityExe : string.Empty,
            ToolExePaths = [],
            Args = item.Arguments ?? string.Empty,
            WorkingDir = workingDir
        };
        var tokenTarget = PathTokenizer.TokenizeForStorage(targetPath, install, config, AppContext.BaseDirectory);
        var tokenWorkdir = PathTokenizer.TokenizeForStorage(workingDir, install, config, AppContext.BaseDirectory);
        var launchKey = LaunchIdentity.BuildLaunchKey(tokenTarget, item.Arguments ?? string.Empty, tokenWorkdir);

        if (registry.Games.Any(g =>
        {
            if (!string.IsNullOrWhiteSpace(g.LaunchKey))
            {
                return LaunchIdentity.IsMatch(g.LaunchKey, launchKey);
            }

            return LaunchIdentity.PreferExePathFallback(item.Arguments ?? string.Empty) &&
                   PathsEqual(g.Install.ExePath, targetPath);
        }))
        {
            return false;
        }

        registry.Games.Add(new GameEntry
        {
            GameKey = Guid.NewGuid(),
            LaunchKey = launchKey,
            ImportedFromShortcut = true,
            ImportedShortcutIconLocation = item.IconLocation ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? Path.GetFileNameWithoutExtension(targetPath)
                : item.DisplayName,
            Install = install,
            Launch = new LaunchSettings
            {
                PreferredMode = "Cache",
                AllowOverlay = true,
                Pinned = false,
                Main = new LaunchContract
                {
                    TargetPath = tokenTarget,
                    Arguments = item.Arguments ?? string.Empty,
                    WorkingDirectory = tokenWorkdir
                }
            },
            Stats = new StatsInfo
            {
                EstimatedBytes = 0,
                LastPlayedUtc = string.Empty,
                LastValidatedUtc = string.Empty,
                LastResult = "Unknown"
            }
        });

        return true;
    }

    public static bool IsImportable(ShortcutScanResult item, bool includeOtherKinds = false)
    {
        if (!item.TargetExists || string.Equals(item.Status, "AlreadyInRegistry", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(item.Kind, "DirectExe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return includeOtherKinds && string.Equals(item.Kind, "Other", StringComparison.OrdinalIgnoreCase);
    }

    private static (string GameFolderPath, string IdentityExePath) InferIdentity(
        ShortcutScanResult item,
        IEnumerable<GameFolderCandidate>? scannedFolders,
        IEnumerable<string>? scanRoots)
    {
        var targetPath = NormalizePath(item.TargetPath);
        var workingDir = NormalizePath(item.WorkingDirectory);

        if (scannedFolders is not null)
        {
            foreach (var folder in scannedFolders)
            {
                var folderPath = NormalizePath(folder.FolderPath);
                if (IsInsideFolder(targetPath, folderPath) || IsInsideFolder(workingDir, folderPath))
                {
                    item.MappedGameFolderPath = folderPath;
                    var identityExe = !string.IsNullOrWhiteSpace(folder.SelectedMainExePath)
                        ? NormalizePath(folder.SelectedMainExePath)
                        : targetPath;
                    return (folderPath, identityExe);
                }
            }

            var normalizedDisplay = NormalizeName(item.DisplayName);
            GameFolderCandidate? byName = null;
            if (!string.IsNullOrWhiteSpace(normalizedDisplay))
            {
                byName = scannedFolders.FirstOrDefault(f =>
                {
                    var folderNorm = NormalizeName(f.FolderName);
                    if (string.IsNullOrWhiteSpace(folderNorm))
                    {
                        return false;
                    }

                    return folderNorm == normalizedDisplay ||
                           folderNorm.Contains(normalizedDisplay, StringComparison.OrdinalIgnoreCase) ||
                           normalizedDisplay.Contains(folderNorm, StringComparison.OrdinalIgnoreCase);
                });
            }
            if (byName is not null)
            {
                var folderPath = NormalizePath(byName.FolderPath);
                item.MappedGameFolderPath = folderPath;
                var identityExe = !string.IsNullOrWhiteSpace(byName.SelectedMainExePath)
                    ? NormalizePath(byName.SelectedMainExePath)
                    : targetPath;
                return (folderPath, identityExe);
            }
        }

        if (scanRoots is not null)
        {
            foreach (var rootRaw in scanRoots)
            {
                var root = PathResolver.Resolve(rootRaw);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var mapped = TryMapToTopFolder(targetPath, root) ?? TryMapToTopFolder(workingDir, root);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    item.MappedGameFolderPath = mapped;
                    return (mapped, targetPath);
                }
            }
        }

        var fallbackFolder = Path.GetDirectoryName(targetPath) ?? string.Empty;
        return (fallbackFolder, targetPath);
    }

    private static string? TryMapToTopFolder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = normalizedPath[normalizedRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var first = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        var topFolder = Path.Combine(normalizedRoot, first);
        return Directory.Exists(topFolder) ? topFolder : null;
    }

    private static bool IsInsideFolder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var normalizedFolder = NormalizePath(folder);
        return normalizedPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static List<ShortcutScanResult> ScanShortcuts(
        string shortcutFolder,
        string relayExePath,
        Registry registry,
        IProgress<ShortcutScanResult>? onItem,
        IProgress<ScanProgress>? onProgress,
        CancellationToken ct)
    {
        var results = new List<ShortcutScanResult>();
        var resolvedFolder = PathResolver.Resolve(shortcutFolder);

        if (string.IsNullOrWhiteSpace(resolvedFolder) || !Directory.Exists(resolvedFolder))
        {
            onProgress?.Report(new ScanProgress { Processed = 0, Total = 0 });
            return results;
        }

        var files = Directory.EnumerateFiles(resolvedFolder, "*.lnk", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolvedRelayExe = NormalizePath(relayExePath);
        var legacyExeSet = new HashSet<string>(
            registry.Games
                .Where(g => string.IsNullOrWhiteSpace(g.LaunchKey))
                .Select(g => NormalizePath(g.Install.ExePath)),
            StringComparer.OrdinalIgnoreCase);
        var launchKeySet = new HashSet<string>(
            registry.Games.Select(g => g.LaunchKey?.Trim() ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)),
            StringComparer.OrdinalIgnoreCase);

        var processed = 0;
        var total = files.Count;
        onProgress?.Report(new ScanProgress { Processed = processed, Total = total });

        foreach (var shortcutPath in files)
        {
            ct.ThrowIfCancellationRequested();

            var result = LoadShortcut(shortcutPath);
            var normalizedTarget = NormalizePath(result.TargetPath);

            result.TargetExists = !string.IsNullOrWhiteSpace(normalizedTarget) && File.Exists(normalizedTarget);
            result.TargetPath = normalizedTarget;
            result.Arguments = LaunchIdentity.NormalizeArguments(result.Arguments);
            result.WorkingDirectory = NormalizePath(result.WorkingDirectory);
            result.LaunchKey = LaunchIdentity.BuildLaunchKey(result.TargetPath, result.Arguments, result.WorkingDirectory);

            if (!result.TargetExists)
            {
                result.Kind = "MissingTarget";
                result.Status = "Missing";
            }
            else if (IsWrapperShortcut(normalizedTarget, resolvedRelayExe))
            {
                result.Kind = "WrapperRelay";
                result.Status = "Wrapper";
            }
            else if (normalizedTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = "DirectExe";
                var isAlreadyByLaunchKey = launchKeySet.Contains(result.LaunchKey);
                var isAlreadyByExeFallback = LaunchIdentity.PreferExePathFallback(result.Arguments) && legacyExeSet.Contains(normalizedTarget);
                result.Status = (isAlreadyByLaunchKey || isAlreadyByExeFallback)
                    ? "AlreadyInRegistry"
                    : "OK";
            }
            else
            {
                result.Kind = "Other";
                result.Status = "NotExe";
            }

            results.Add(result);
            onItem?.Report(result);

            processed++;
            onProgress?.Report(new ScanProgress { Processed = processed, Total = total });
        }

        return results;
    }

    private static bool IsWrapperShortcut(string targetPath, string relayExePath)
    {
        if (targetPath.EndsWith("\\Relay.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(relayExePath)
               && string.Equals(targetPath, relayExePath, StringComparison.OrdinalIgnoreCase);
    }

    private static ShortcutScanResult LoadShortcut(string shortcutPath)
    {
        var result = new ShortcutScanResult
        {
            ShortcutPath = shortcutPath,
            DisplayName = Path.GetFileNameWithoutExtension(shortcutPath)
        };

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return result;
        }

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return result;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            if (shortcut is null)
            {
                return result;
            }

            result.TargetPath = ReadComProperty(shortcut, "TargetPath");
            result.Arguments = ReadComProperty(shortcut, "Arguments");
            result.WorkingDirectory = ReadComProperty(shortcut, "WorkingDirectory");
            result.IconLocation = ReadComProperty(shortcut, "IconLocation");
            return result;
        }
        catch
        {
            return result;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string ReadComProperty(object target, string propertyName)
    {
        try
        {
            var value = target.GetType().InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                target,
                args: null);

            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ShortcutGenerationResult GenerateShortcuts(
        Config config,
        Registry registry,
        bool cleanOrphans,
        bool isWrapperMode)
    {
        var outputRoot = PathResolver.Resolve(config.Paths.ShortcutOutputRoot?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return new ShortcutGenerationResult(false, 0, 0, 0, "ShortcutOutputRoot is empty.");
        }

        Directory.CreateDirectory(outputRoot);

        var expectedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedCount = 0;
        var skippedCount = 0;

        foreach (var game in registry.Games)
        {
            if (!TryBuildShortcutSpec(game, isWrapperMode, out var spec))
            {
                skippedCount++;
                continue;
            }

            var fileName = GetUniqueFileName(outputRoot, game.DisplayName, expectedFileNames);
            var shortcutPath = Path.Combine(outputRoot, fileName);

            CreateShortcut(
                shortcutPath,
                spec.TargetPath,
                spec.Arguments,
                spec.WorkingDirectory,
                spec.IconLocation);

            expectedFileNames.Add(fileName);
            generatedCount++;
        }

        var deletedOrphans = 0;
        if (cleanOrphans)
        {
            deletedOrphans = DeleteOrphans(outputRoot, expectedFileNames);
        }

        var modeText = isWrapperMode ? "wrapper" : "direct";
        var message = $"Generated {generatedCount} {modeText} shortcuts";
        if (skippedCount > 0)
        {
            message += $", skipped {skippedCount}";
        }

        if (cleanOrphans)
        {
            message += $", deleted {deletedOrphans} orphan(s).";
        }
        else
        {
            message += ".";
        }

        return new ShortcutGenerationResult(true, generatedCount, skippedCount, deletedOrphans, message);
    }

    private static ShortcutGenerationResult GenerateMainWrapperShortcuts(Config config, Registry registry)
    {
        var baseOutput = PathResolver.Resolve(config.Paths.ShortcutOutputRoot?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(baseOutput))
        {
            return new ShortcutGenerationResult(false, 0, 0, 0, "ShortcutOutputRoot is empty.");
        }

        var outputRoot = Path.Combine(baseOutput, "Main");
        Directory.CreateDirectory(outputRoot);

        var relayExe = ResolveRelayExePath();
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generated = 0;
        var skipped = 0;

        foreach (var game in registry.Games)
        {
            if (string.IsNullOrWhiteSpace(game.GameKey.ToString()))
            {
                skipped++;
                continue;
            }

            var shortcutName = GetUniqueFileName(outputRoot, game.DisplayName, expected);
            var shortcutPath = Path.Combine(outputRoot, shortcutName);
            var icon = File.Exists(game.Install.ExePath) ? game.Install.ExePath : string.Empty;

            CreateShortcut(
                shortcutPath,
                relayExe,
                $"launch --key {game.GameKey}",
                AppContext.BaseDirectory,
                icon);

            expected.Add(shortcutName);
            generated++;
        }

        return new ShortcutGenerationResult(
            true,
            generated,
            skipped,
            0,
            $"Generated {generated} main wrapper shortcuts in {outputRoot}.");
    }

    private static ShortcutGenerationResult GenerateToolsWrapperShortcuts(Config config, Registry registry)
    {
        var baseOutput = PathResolver.Resolve(config.Paths.ShortcutOutputRoot?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(baseOutput))
        {
            return new ShortcutGenerationResult(false, 0, 0, 0, "ShortcutOutputRoot is empty.");
        }

        var outputRoot = Path.Combine(baseOutput, "Tools");
        Directory.CreateDirectory(outputRoot);

        var relayExe = ResolveRelayExePath();
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generated = 0;
        var skipped = 0;

        foreach (var game in registry.Games)
        {
            foreach (var toolPathRaw in game.Install.ToolExePaths ?? [])
            {
                var toolPath = NormalizePath(toolPathRaw);
                if (string.IsNullOrWhiteSpace(toolPath))
                {
                    skipped++;
                    continue;
                }

                var toolFile = Path.GetFileNameWithoutExtension(toolPath);
                var displayName = $"{game.DisplayName} - {toolFile}";
                var shortcutName = GetUniqueFileName(outputRoot, displayName, expected);
                var shortcutPath = Path.Combine(outputRoot, shortcutName);

                var icon = File.Exists(toolPath) ? toolPath : string.Empty;
                var args = $"launch --key {game.GameKey} --tool \"{toolPath}\"";

                CreateShortcut(
                    shortcutPath,
                    relayExe,
                    args,
                    AppContext.BaseDirectory,
                    icon);

                expected.Add(shortcutName);
                generated++;
            }
        }

        return new ShortcutGenerationResult(
            true,
            generated,
            skipped,
            0,
            $"Generated {generated} tool wrapper shortcuts in {outputRoot}.");
    }

    private static bool TryBuildShortcutSpec(GameEntry game, bool isWrapperMode, out ShortcutSpec spec)
    {
        spec = default;

        if (isWrapperMode)
        {
            var relayExe = ResolveRelayExePath();

            var iconLocation = string.Empty;
            if (!string.IsNullOrWhiteSpace(game.Install.ExePath) && File.Exists(game.Install.ExePath))
            {
                iconLocation = game.Install.ExePath;
            }

            spec = new ShortcutSpec(
                relayExe,
                $"launch --key {game.GameKey}",
                AppContext.BaseDirectory,
                iconLocation);

            return true;
        }

        if (string.IsNullOrWhiteSpace(game.Install.ExePath))
        {
            return false;
        }

        var workingDirectory = game.Install.WorkingDir;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = !string.IsNullOrWhiteSpace(game.Install.BaseFolder)
                ? game.Install.BaseFolder
                : Path.GetDirectoryName(game.Install.ExePath) ?? string.Empty;
        }

        spec = new ShortcutSpec(
            game.Install.ExePath,
            game.Install.Args ?? string.Empty,
            workingDirectory,
            game.Install.ExePath);

        return true;
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string iconLocation)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available.");

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Failed to create WScript.Shell.");

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            if (shortcut is null)
            {
                throw new InvalidOperationException("CreateShortcut returned null.");
            }

            SetComProperty(shortcut, "TargetPath", targetPath);
            SetComProperty(shortcut, "Arguments", arguments ?? string.Empty);
            SetComProperty(shortcut, "WorkingDirectory", workingDirectory ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(iconLocation))
            {
                SetComProperty(shortcut, "IconLocation", iconLocation);
            }

            shortcut.GetType().InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void SetComProperty(object target, string propertyName, string value)
    {
        target.GetType().InvokeMember(
            propertyName,
            System.Reflection.BindingFlags.SetProperty,
            binder: null,
            target,
            args: [value]);
    }

    private static int DeleteOrphans(string outputRoot, HashSet<string> expectedFileNames)
    {
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(outputRoot, "*.lnk", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (expectedFileNames.Contains(fileName))
            {
                continue;
            }

            File.Delete(path);
            deleted++;
        }

        return deleted;
    }

    private static string GetUniqueFileName(
        string outputRoot,
        string displayName,
        HashSet<string> expectedFileNames)
    {
        var baseName = SanitizeFileName(displayName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Game";
        }

        var index = 1;
        while (true)
        {
            var candidateName = index == 1
                ? $"{baseName}.lnk"
                : $"{baseName} ({index}).lnk";

            var candidatePath = Path.Combine(outputRoot, candidateName);
            if (!expectedFileNames.Contains(candidateName) && !File.Exists(candidatePath))
            {
                return candidateName;
            }

            index++;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var value = name ?? string.Empty;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim();
    }

    private static string ResolveRelayExePath()
    {
        var relayExe = Path.Combine(AppContext.BaseDirectory, "Relay.exe");
        if (!File.Exists(relayExe) && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            relayExe = Environment.ProcessPath;
        }

        return relayExe;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGameLaunchKey(GameEntry game)
    {
        return game.LaunchKey ?? string.Empty;
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

    private readonly record struct ShortcutSpec(
        string TargetPath,
        string Arguments,
        string WorkingDirectory,
        string IconLocation);
}

public sealed record ShortcutGenerationResult(
    bool Success,
    int GeneratedCount,
    int SkippedCount,
    int DeletedOrphans,
    string Message);
