using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Relay.Data;
using Relay.Data.Models;

namespace Relay.Core;

public sealed class Scanner
{
    private static readonly string[] PenalizedTokens =
    [
        "setup", "config", "settings", "launcher", "uninstall", "unins", "crash",
        "report", "benchmark", "server", "editor", "tool", "mod", "patch"
    ];

    public async Task<List<GameFolderCandidate>> ScanGameFoldersAsync(
        IEnumerable<string> scanRoots,
        Registry registry,
        Config config,
        ScanCache cache,
        bool incremental,
        bool skipKnownRegistry,
        bool forceFullRescan,
        IProgress<GameFolderCandidate>? onFolder,
        IProgress<ScanMetrics>? onMetrics,
        CancellationToken ct)
    {
        return await Task.Run(() =>
            ScanGameFoldersInternal(
                scanRoots,
                registry,
                config,
                cache,
                incremental,
                skipKnownRegistry,
                forceFullRescan,
                onFolder,
                onMetrics,
                ct), ct);
    }

    public bool HideInRegistry(Registry registry, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var normalized = NormalizePath(exePath);
        if (registry.HiddenExecutables.Any(h => PathsEqual(h, normalized)))
        {
            return false;
        }

        registry.HiddenExecutables.Add(normalized);
        return true;
    }

    public bool SetMain(GameFolderCandidate folder, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var candidate = folder.Exes.FirstOrDefault(e => PathsEqual(e.ExePath, exePath));
        if (candidate is null || string.Equals(candidate.Kind, "Hidden", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        folder.SelectedMainExePath = candidate.ExePath;
        folder.SelectedToolExePaths.Remove(candidate.ExePath);
        candidate.IsToolSelected = false;

        foreach (var other in folder.Exes.Where(e => !PathsEqual(e.ExePath, candidate.ExePath)))
        {
            if (string.Equals(other.Kind, "MainCandidate", StringComparison.OrdinalIgnoreCase))
            {
                other.Kind = "ToolCandidate";
                if (string.IsNullOrWhiteSpace(other.Reason))
                {
                    other.Reason = "Not selected as main";
                }
            }
        }

        if (string.Equals(candidate.Kind, "SmallExe", StringComparison.OrdinalIgnoreCase) ||
            candidate.Reason.Contains("MinExeBytes", StringComparison.OrdinalIgnoreCase) ||
            candidate.Reason.Contains("IgnoredByRule", StringComparison.OrdinalIgnoreCase))
        {
            candidate.IsMainOverride = true;
            if (string.IsNullOrWhiteSpace(candidate.Reason))
            {
                candidate.Reason = "MainOverride";
            }
            else if (!candidate.Reason.Contains("MainOverride", StringComparison.OrdinalIgnoreCase))
            {
                candidate.Reason = $"{candidate.Reason}; MainOverride";
            }
        }

        candidate.Kind = "MainCandidate";
        return true;
    }

    public void ToggleTool(GameFolderCandidate folder, string exePath, bool selected)
    {
        var normalized = NormalizePath(exePath);
        if (selected)
        {
            folder.SelectedToolExePaths.Add(normalized);
        }
        else
        {
            folder.SelectedToolExePaths.Remove(normalized);
        }

        var candidate = folder.Exes.FirstOrDefault(e => PathsEqual(e.ExePath, normalized));
        if (candidate is not null)
        {
            candidate.IsToolSelected = selected;
            if (selected && !string.Equals(candidate.Kind, "Excluded", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Kind, "Hidden", StringComparison.OrdinalIgnoreCase))
            {
                candidate.Kind = "ToolCandidate";
            }
        }
    }

    public bool AddMainForFolderToRegistry(GameFolderCandidate folder, Registry registry, Config config)
    {
        if (string.IsNullOrWhiteSpace(folder.SelectedMainExePath))
        {
            return false;
        }

        var mainExe = NormalizePath(folder.SelectedMainExePath);
        var folderPath = NormalizePath(folder.FolderPath);
        var workdir = Path.GetDirectoryName(mainExe) ?? folderPath;
        var launchContractForIdentity = new LaunchContract
        {
            TargetPath = PathTokenizer.TokenizeForStorage(mainExe, new InstallInfo
            {
                GameFolderPath = folderPath,
                BaseFolder = folderPath
            }, config, AppContext.BaseDirectory),
            Arguments = string.Empty,
            WorkingDirectory = PathTokenizer.TokenizeForStorage(workdir, new InstallInfo
            {
                GameFolderPath = folderPath,
                BaseFolder = folderPath
            }, config, AppContext.BaseDirectory)
        };
        var launchKey = LaunchIdentity.BuildLaunchKey(
            launchContractForIdentity.TargetPath,
            launchContractForIdentity.Arguments,
            launchContractForIdentity.WorkingDirectory);

        var existing = registry.Games.FirstOrDefault(g =>
        {
            var existingKey = g.LaunchKey?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(existingKey))
            {
                return LaunchIdentity.IsMatch(existingKey, launchKey);
            }

            return (!string.IsNullOrWhiteSpace(g.Install.GameFolderPath) && PathsEqual(g.Install.GameFolderPath, folderPath)) ||
                   (LaunchIdentity.PreferExePathFallback(g.Launch.Main.Arguments) && PathsEqual(g.Install.ExePath, mainExe));
        });

        if (existing is null)
        {
            existing = new GameEntry
            {
                GameKey = Guid.NewGuid(),
                DisplayName = string.IsNullOrWhiteSpace(folder.FolderName) ? "Game" : folder.FolderName,
                Install = new InstallInfo(),
                Launch = new LaunchSettings
                {
                    PreferredMode = "Cache",
                    AllowOverlay = true,
                    Pinned = false
                },
                Stats = new StatsInfo
                {
                    EstimatedBytes = 0,
                    LastResult = "Unknown"
                }
            };

            registry.Games.Add(existing);
        }

        existing.Install.GameFolderPath = folderPath;
        existing.Install.BaseFolder = folderPath;
        existing.Install.ExePath = mainExe;
        existing.Install.WorkingDir = workdir;
        existing.Install.ToolExePaths = folder.SelectedToolExePaths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        existing.LaunchKey = launchKey;

        existing.Launch.Main = new LaunchContract
        {
            TargetPath = PathTokenizer.TokenizeForStorage(mainExe, existing.Install, config, AppContext.BaseDirectory),
            Arguments = existing.Install.Args ?? string.Empty,
            WorkingDirectory = PathTokenizer.TokenizeForStorage(existing.Install.WorkingDir, existing.Install, config, AppContext.BaseDirectory)
        };

        foreach (var tool in existing.Install.ToolExePaths)
        {
            existing.Launch.Tools[tool] = new LaunchContract
            {
                TargetPath = PathTokenizer.TokenizeForStorage(tool, existing.Install, config, AppContext.BaseDirectory),
                Arguments = string.Empty,
                WorkingDirectory = PathTokenizer.TokenizeForStorage(Path.GetDirectoryName(tool) ?? folderPath, existing.Install, config, AppContext.BaseDirectory)
            };
        }

        return true;
    }

    public (int Added, int Skipped) AddAllMainToRegistry(IEnumerable<GameFolderCandidate> folders, Registry registry, Config config)
    {
        var added = 0;
        var skipped = 0;

        foreach (var folder in folders)
        {
            if (AddMainForFolderToRegistry(folder, registry, config))
            {
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return (added, skipped);
    }

    public bool AddToolsForFolderToRegistry(GameFolderCandidate folder, Registry registry, Config config)
    {
        var folderPath = NormalizePath(folder.FolderPath);
        var existing = registry.Games.FirstOrDefault(g =>
            (!string.IsNullOrWhiteSpace(g.Install.GameFolderPath) && PathsEqual(g.Install.GameFolderPath, folderPath)) ||
            (!string.IsNullOrWhiteSpace(folder.SelectedMainExePath) && PathsEqual(g.Install.ExePath, folder.SelectedMainExePath)));

        if (existing is null)
        {
            return false;
        }

        var tools = folder.SelectedToolExePaths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        existing.Install.ToolExePaths = tools;
        foreach (var tool in tools)
        {
            existing.Launch.Tools[tool] = new LaunchContract
            {
                TargetPath = PathTokenizer.TokenizeForStorage(tool, existing.Install, config, AppContext.BaseDirectory),
                Arguments = string.Empty,
                WorkingDirectory = PathTokenizer.TokenizeForStorage(Path.GetDirectoryName(tool) ?? folderPath, existing.Install, config, AppContext.BaseDirectory)
            };
        }

        return true;
    }

    private static List<GameFolderCandidate> ScanGameFoldersInternal(
        IEnumerable<string> scanRoots,
        Registry registry,
        Config config,
        ScanCache cache,
        bool incremental,
        bool skipKnownRegistry,
        bool forceFullRescan,
        IProgress<GameFolderCandidate>? onFolder,
        IProgress<ScanMetrics>? onMetrics,
        CancellationToken ct)
    {
        var result = new List<GameFolderCandidate>();
        var metrics = new ScanMetrics();

        var roots = (scanRoots ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(PathResolver.Resolve)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

        cache.ScanRootsSnapshot = roots;

        var hiddenSet = new HashSet<string>(
            registry.HiddenExecutables.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);

        var scanning = config.Scanning ?? new ScanningConfig();
        var excludedPatterns = (scanning.ExcludedExePatterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        var excludedFolderNames = new HashSet<string>(
            (scanning.ExcludedFolderNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);

        var minBytes = scanning.MinExeBytes > 0 ? scanning.MinExeBytes : 524288;
        var scannerRules = config.ScannerRules ?? new ScannerRulesConfig();
        var ignoredExeNamePatterns = (scannerRules.ExeIgnoreNamePatterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        var ignoredFolderPatterns = (scannerRules.ExeIgnoreFolderNamePatterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        foreach (var root in roots)
        {
            IEnumerable<string> folders;
            try
            {
                folders = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                continue;
            }

            foreach (var folderPathRaw in folders)
            {
                ct.ThrowIfCancellationRequested();

                var folderPath = NormalizePath(folderPathRaw);
                var known = IsKnownFolder(folderPath, registry);
                var fingerprint = ComputeFolderFingerprint(folderPath);

                GameFolderCandidate folder;
                if (!forceFullRescan && incremental && cache.Folders.TryGetValue(folderPath, out var cached) &&
                    string.Equals(cached.FolderFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    folder = RestoreFromCache(cached);
                    folder.Source = "Cache";
                    folder.State = known ? "Known" : "Cached";
                    metrics.CachedFolders++;
                }
                else if (!forceFullRescan && skipKnownRegistry && known)
                {
                    if (cache.Folders.TryGetValue(folderPath, out var knownCached))
                    {
                        folder = RestoreFromCache(knownCached);
                        folder.Source = "Cache";
                    }
                    else
                    {
                        folder = new GameFolderCandidate
                        {
                            FolderPath = folderPath,
                            FolderName = Path.GetFileName(folderPath),
                            State = "Known",
                            Source = "Skipped"
                        };
                    }

                    folder.State = "Known";
                    metrics.SkippedKnownFolders++;
                }
                else
                {
                    folder = BuildFolderCandidate(
                        folderPath,
                        hiddenSet,
                        excludedPatterns,
                        excludedFolderNames,
                        ignoredExeNamePatterns,
                        ignoredFolderPatterns,
                        minBytes);

                    folder.Source = "Disk";
                    folder.State = folder.ValidExeCount > 0 ? "New" : "NoValidExe";

                    cache.Folders[folderPath] = ToCacheEntry(folder, fingerprint);
                }

                ApplyShortcutsMapping(folder, registry);

                result.Add(folder);
                UpdateMetrics(metrics, folder);

                onFolder?.Report(folder);
                onMetrics?.Report(CloneMetrics(metrics));
            }
        }

        return result;
    }

    private static bool IsKnownFolder(string folderPath, Registry registry)
    {
        foreach (var game in registry.Games)
        {
            if (!string.IsNullOrWhiteSpace(game.Install.GameFolderPath) &&
                PathsEqual(game.Install.GameFolderPath, folderPath))
            {
                return true;
            }

            var exePath = NormalizePath(game.Install.ExePath);
            if (!string.IsNullOrWhiteSpace(exePath) && exePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var launchTarget = NormalizePath(game.Launch.Main.TargetPath);
            if (!string.IsNullOrWhiteSpace(launchTarget) && launchTarget.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyShortcutsMapping(GameFolderCandidate folder, Registry registry)
    {
        var folderPath = NormalizePath(folder.FolderPath);

        foreach (var game in registry.Games)
        {
            var launchTarget = NormalizePath(game.Launch.Main.TargetPath);
            var workingDir = NormalizePath(game.Launch.Main.WorkingDirectory);
            var displayNorm = NormalizeName(game.DisplayName);
            var folderNorm = NormalizeName(folder.FolderName);

            var matched = (!string.IsNullOrWhiteSpace(launchTarget) && launchTarget.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                          || (!string.IsNullOrWhiteSpace(workingDir) && workingDir.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                          || (!string.IsNullOrWhiteSpace(displayNorm) && displayNorm == folderNorm);

            if (!matched)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(game.Install.GameFolderPath))
            {
                game.Install.GameFolderPath = folderPath;
            }

            if (string.IsNullOrWhiteSpace(game.Install.ExePath) && !string.IsNullOrWhiteSpace(folder.SelectedMainExePath))
            {
                game.Install.ExePath = folder.SelectedMainExePath;
                game.Install.BaseFolder = folderPath;
                game.Install.WorkingDir = Path.GetDirectoryName(folder.SelectedMainExePath) ?? folderPath;
            }
        }
    }

    private static FolderCacheEntry ToCacheEntry(GameFolderCandidate folder, string fingerprint)
    {
        return new FolderCacheEntry
        {
            FolderPath = folder.FolderPath,
            FolderName = folder.FolderName,
            LastScannedUtc = DateTime.UtcNow.ToString("O"),
            FolderFingerprint = fingerprint,
            SelectedMainExePath = folder.SelectedMainExePath,
            ExeCandidates = folder.Exes.Select(e => new ExeCandidate
            {
                ExePath = e.ExePath,
                SuggestedName = e.SuggestedName,
                SizeBytes = e.SizeBytes,
                Kind = e.Kind,
                Reason = e.Reason,
                IsToolSelected = e.IsToolSelected
            }).ToList()
        };
    }

    private static GameFolderCandidate RestoreFromCache(FolderCacheEntry cache)
    {
        var folder = new GameFolderCandidate
        {
            FolderPath = cache.FolderPath,
            FolderName = cache.FolderName,
            SelectedMainExePath = cache.SelectedMainExePath,
            Exes = cache.ExeCandidates.Select(e => new ExeCandidate
            {
                ExePath = e.ExePath,
                SuggestedName = e.SuggestedName,
                SizeBytes = e.SizeBytes,
                Kind = e.Kind,
                Reason = e.Reason,
                IsToolSelected = e.IsToolSelected
            }).ToList()
        };

        folder.SelectedToolExePaths = folder.Exes
            .Where(e => e.IsToolSelected)
            .Select(e => e.ExePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        folder.ExcludedExeCount = folder.Exes.Count(e => string.Equals(e.Kind, "Excluded", StringComparison.OrdinalIgnoreCase));
        folder.HiddenExeCount = folder.Exes.Count(e => string.Equals(e.Kind, "Hidden", StringComparison.OrdinalIgnoreCase));
        folder.ValidExeCount = folder.Exes.Count(e =>
            !string.Equals(e.Kind, "Excluded", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(e.Kind, "Hidden", StringComparison.OrdinalIgnoreCase));

        return folder;
    }

    private static string ComputeFolderFingerprint(string folderPath)
    {
        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories).ToList();
            var maxTicks = files.Count == 0
                ? Directory.GetLastWriteTimeUtc(folderPath).Ticks
                : files.Max(f => File.GetLastWriteTimeUtc(f).Ticks);

            return $"{files.Count}:{maxTicks}";
        }
        catch
        {
            return Directory.GetLastWriteTimeUtc(folderPath).Ticks.ToString();
        }
    }

    private static GameFolderCandidate BuildFolderCandidate(
        string folderPath,
        HashSet<string> hiddenSet,
        List<string> excludedPatterns,
        HashSet<string> excludedFolderNames,
        List<string> ignoredExeNamePatterns,
        List<string> ignoredFolderPatterns,
        long minBytes)
    {
        var folder = new GameFolderCandidate
        {
            FolderPath = NormalizePath(folderPath),
            FolderName = Path.GetFileName(folderPath)
        };

        IEnumerable<string> exePaths;
        try
        {
            exePaths = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories);
        }
        catch
        {
            exePaths = [];
        }

        var eligible = new List<(ExeCandidate Candidate, double Score)>();

        foreach (var exePathRaw in exePaths)
        {
            var exePath = NormalizePath(exePathRaw);
            var candidate = new ExeCandidate
            {
                ExePath = exePath,
                SuggestedName = BuildSuggestedName(exePath)
            };

            var fileName = Path.GetFileName(exePath);
            var directory = Path.GetDirectoryName(exePath) ?? string.Empty;
            var relative = BuildRelativePath(folder.FolderPath, directory);

            if (IsFolderExcludedRelative(folder.FolderPath, directory, excludedFolderNames))
            {
                candidate.Kind = "Excluded";
                candidate.Reason = "Excluded by folder name";
                folder.ExcludedExeCount++;
                folder.Exes.Add(candidate);
                continue;
            }

            if (excludedPatterns.Any(p => WildcardMatch(fileName, p)))
            {
                candidate.Kind = "Excluded";
                candidate.Reason = "Excluded by executable pattern";
                folder.ExcludedExeCount++;
                folder.Exes.Add(candidate);
                continue;
            }

            var ruleReason = GetIgnoreRuleReason(fileName, relative, ignoredExeNamePatterns, ignoredFolderPatterns);
            if (!string.IsNullOrWhiteSpace(ruleReason))
            {
                candidate.Kind = "ToolCandidate";
                candidate.Reason = ruleReason;
            }

            long size;
            try
            {
                size = new FileInfo(exePath).Length;
            }
            catch
            {
                candidate.Kind = "Excluded";
                candidate.Reason = "Could not read file metadata";
                folder.ExcludedExeCount++;
                folder.Exes.Add(candidate);
                continue;
            }

            candidate.SizeBytes = size;

            if (size < minBytes)
            {
                candidate.Kind = "SmallExe";
                candidate.Reason = $"Below MinExeBytes ({minBytes})";
                folder.ExcludedExeCount++;
                folder.Exes.Add(candidate);
                continue;
            }

            if (hiddenSet.Contains(exePath))
            {
                candidate.Kind = "Hidden";
                candidate.Reason = "Path exists in HiddenExecutables";
                folder.HiddenExeCount++;
                folder.Exes.Add(candidate);
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Reason))
            {
                candidate.Kind = IsLikelyTool(fileName)
                    ? "ToolCandidate"
                    : "MainCandidate";
                candidate.Reason = candidate.Kind == "ToolCandidate"
                    ? "Filename suggests tool/utility"
                    : string.Empty;
            }

            if (!candidate.Reason.Contains("IgnoredByRule", StringComparison.OrdinalIgnoreCase))
            {
                var score = ScoreMainCandidate(exePath, candidate, folder.FolderName, folder.FolderPath);
                eligible.Add((candidate, score));
            }
            folder.ValidExeCount++;
            folder.Exes.Add(candidate);
        }

        if (eligible.Count > 0)
        {
            var best = eligible.OrderByDescending(e => e.Score).First().Candidate;
            folder.SelectedMainExePath = best.ExePath;

            foreach (var exe in folder.Exes)
            {
                if (PathsEqual(exe.ExePath, best.ExePath))
                {
                    exe.Kind = "MainCandidate";
                    exe.Reason = "Selected as main";
                }
                else if (!string.Equals(exe.Kind, "Excluded", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(exe.Kind, "Hidden", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(exe.Kind, "ToolCandidate", StringComparison.OrdinalIgnoreCase))
                    {
                        exe.Kind = "ToolCandidate";
                    }
                }
            }
        }

        folder.Exes = folder.Exes
            .OrderBy(e => e.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.SuggestedName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return folder;
    }

    private static void UpdateMetrics(ScanMetrics metrics, GameFolderCandidate folder)
    {
        metrics.TotalFolders++;
        metrics.TotalExeCandidates += folder.Exes.Count;
        metrics.Excluded += folder.ExcludedExeCount;
        metrics.Hidden += folder.HiddenExeCount;
        if (folder.ValidExeCount > 0)
        {
            metrics.FoldersWithValidExe++;
        }

        if (!string.IsNullOrWhiteSpace(folder.SelectedMainExePath))
        {
            metrics.MainSelected++;
        }

        metrics.ToolsSelected += folder.SelectedToolExePaths.Count;
    }

    private static bool IsFolderExcludedRelative(string folderRoot, string currentDirectory, HashSet<string> excludedFolderNames)
    {
        if (excludedFolderNames.Count == 0)
        {
            return false;
        }

        var root = NormalizePath(folderRoot);
        var current = NormalizePath(currentDirectory);

        if (!current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = current[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return false;
        }

        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => excludedFolderNames.Contains(s));
    }

    private static string BuildRelativePath(string folderRoot, string currentDirectory)
    {
        var root = NormalizePath(folderRoot);
        var current = NormalizePath(currentDirectory);

        if (!current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return current[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetIgnoreRuleReason(
        string fileName,
        string relativeDirectory,
        List<string> ignoredExeNamePatterns,
        List<string> ignoredFolderPatterns)
    {
        foreach (var pattern in ignoredExeNamePatterns)
        {
            if (PatternMatch(fileName, pattern))
            {
                return $"IgnoredByRule: NamePattern={pattern}";
            }
        }

        if (!string.IsNullOrWhiteSpace(relativeDirectory))
        {
            var segments = relativeDirectory.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                foreach (var pattern in ignoredFolderPatterns)
                {
                    if (PatternMatch(segment, pattern))
                    {
                        return $"IgnoredByRule: FolderPattern={pattern}";
                    }
                }
            }
        }

        return string.Empty;
    }

    private static double ScoreMainCandidate(string exePath, ExeCandidate candidate, string folderName, string folderPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(exePath);
        var score = 1000d;

        foreach (var token in PenalizedTokens)
        {
            if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score -= 350;
            }
        }

        var relative = NormalizePath(Path.GetDirectoryName(exePath) ?? string.Empty)
            .Replace(NormalizePath(folderPath), string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var depth = string.IsNullOrWhiteSpace(relative)
            ? 0
            : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;

        score -= depth * 45;
        score += Math.Min(candidate.SizeBytes / 1_000_000d, 500d);

        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(fvi.ProductName) &&
                fvi.ProductName.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                score += 250;
            }

            if (!string.IsNullOrWhiteSpace(fvi.FileDescription) &&
                fvi.FileDescription.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                score += 200;
            }
        }
        catch
        {
        }

        if (fileName.Contains(folderName, StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        return score;
    }

    private static bool IsLikelyTool(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return PenalizedTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static ScanMetrics CloneMetrics(ScanMetrics metrics)
    {
        return new ScanMetrics
        {
            TotalFolders = metrics.TotalFolders,
            FoldersWithValidExe = metrics.FoldersWithValidExe,
            TotalExeCandidates = metrics.TotalExeCandidates,
            MainSelected = metrics.MainSelected,
            ToolsSelected = metrics.ToolsSelected,
            Excluded = metrics.Excluded,
            Hidden = metrics.Hidden,
            CachedFolders = metrics.CachedFolders,
            SkippedKnownFolders = metrics.SkippedKnownFolders
        };
    }

    private static string BuildSuggestedName(string exePath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(version.ProductName))
            {
                return version.ProductName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(version.FileDescription))
            {
                return version.FileDescription.Trim();
            }
        }
        catch
        {
        }

        var name = Path.GetFileNameWithoutExtension(exePath);
        name = Regex.Replace(name, "(?i)\\b(x64|win64|launcher|shipping|release|final)\\b", " ");
        name = Regex.Replace(name, "[_\\.-]+", " ");
        name = Regex.Replace(name, "\\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(exePath)
            : name;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool PatternMatch(string input, string pattern)
    {
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            var regexPattern = pattern["regex:".Length..];
            if (string.IsNullOrWhiteSpace(regexPattern))
            {
                return false;
            }

            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return WildcardMatch(input, pattern);
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

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
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

    private static string GetGameLaunchKey(GameEntry game)
    {
        return game.LaunchKey ?? string.Empty;
    }
}
