using System.Diagnostics;
using System.IO;
using Relay.Data.Models;
using Relay.Services;

namespace Relay.Core;

public sealed class GameLauncher(
    ConfigService configService,
    RegistryService registryService,
    LoggingService logger)
{
    public async Task<int> LaunchByKeyAsync(Guid gameKey, string? toolExePath = null, IProgress<LaunchOverlayUpdate>? progress = null)
    {
        var config = await configService.LoadAsync();
        var registry = await registryService.LoadAsync();

        if (!Validator.ValidateConfig(config, logger))
        {
            logger.Error("Config invalid during launch.");
            return (int)ExitCodes.ConfigInvalid;
        }

        var game = registry.Games.FirstOrDefault(g => g.GameKey == gameKey);
        if (game is null)
        {
            logger.Warn($"Game key not found: {gameKey}");
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Failed,
                Message = "Failed: game key not found",
                ExceptionText = $"No game found for key {gameKey}."
            });
            return (int)ExitCodes.GameNotFound;
        }

        if (string.IsNullOrWhiteSpace(game.LaunchKey))
        {
            game.LaunchKey = LaunchIdentity.BuildLaunchKeyForGame(game, config, AppContext.BaseDirectory);
            await registryService.SaveAsync(registry);
        }

        var gameFolder = !string.IsNullOrWhiteSpace(game.Install.GameFolderPath)
            ? game.Install.GameFolderPath
            : game.Install.BaseFolder;
        var relayDir = AppContext.BaseDirectory;

        LaunchContract contract;

        if (!string.IsNullOrWhiteSpace(toolExePath))
        {
            var resolvedTool = NormalizePath(toolExePath);
            var resolvedGameFolder = NormalizePath(gameFolder);

            if (string.IsNullOrWhiteSpace(resolvedTool) || !File.Exists(resolvedTool))
            {
                logger.Error($"Tool executable missing: {toolExePath}");
                return (int)ExitCodes.ExeMissing;
            }

            if (!string.IsNullOrWhiteSpace(resolvedGameFolder) &&
                !resolvedTool.StartsWith(resolvedGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                logger.Error($"Tool path is outside game folder. Tool={resolvedTool}, GameFolder={resolvedGameFolder}");
                return (int)ExitCodes.ConfigInvalid;
            }

            if (game.Launch.Tools.TryGetValue(resolvedTool, out var toolContract) && toolContract.IsValid)
            {
                contract = toolContract;
            }
            else
            {
                contract = new LaunchContract
                {
                    TargetPath = resolvedTool,
                    Arguments = string.Empty,
                    WorkingDirectory = Path.GetDirectoryName(resolvedTool) ?? resolvedGameFolder
                };
            }
        }
        else
        {
            if (game.Launch.Main.IsValid)
            {
                contract = game.Launch.Main;
            }
            else
            {
                contract = new LaunchContract
                {
                    TargetPath = game.Install.ExePath,
                    Arguments = game.Install.Args ?? string.Empty,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(game.Install.WorkingDir)
                        ? game.Install.WorkingDir
                        : Path.GetDirectoryName(game.Install.ExePath) ?? game.Install.BaseFolder
                };
            }
        }

        var rawTarget = contract.TargetPath;
        var rawWorkdir = contract.WorkingDirectory;
        var rawArgs = contract.Arguments ?? string.Empty;

        progress?.Report(new LaunchOverlayUpdate
        {
            Stage = LaunchOverlayStage.Resolving,
            DisplayName = game.DisplayName,
            Message = "Resolving paths...",
            TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
            RawTarget = rawTarget,
            RawArgs = rawArgs,
            RawWorkDir = rawWorkdir
        });

        if (!PathTokenResolver.TryResolve(rawTarget, game.Install, contract, config, relayDir, out var launchTarget, out var targetWarning))
        {
            logger.Warn($"Invalid launch target for {game.DisplayName}: raw={rawTarget}; warning={targetWarning}");
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Failed,
                DisplayName = game.DisplayName,
                Message = "Failed",
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ExceptionText = targetWarning
            });
            return (int)ExitCodes.ConfigInvalid;
        }

        if (string.IsNullOrWhiteSpace(launchTarget) || !File.Exists(launchTarget))
        {
            game.Stats.LastResult = "MissingExe";
            game.Stats.LastValidatedUtc = DateTime.UtcNow.ToString("O");
            await registryService.SaveAsync(registry);

            logger.Error($"Launch target missing for {game.DisplayName}: {launchTarget}");
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Failed,
                DisplayName = game.DisplayName,
                Message = "Failed: target missing",
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ResolvedTarget = launchTarget,
                ResolvedArgs = rawArgs,
                TargetExists = false,
                ExceptionText = $"Resolved target does not exist: {launchTarget}"
            });
            return (int)ExitCodes.ExeMissing;
        }
        logger.Info($"Launch resolved target exists: {launchTarget}");

        if (!PathTokenResolver.TryResolve(rawWorkdir, game.Install, contract, config, relayDir, out var workingDirectory, out var workDirWarning))
        {
            logger.Warn($"Invalid launch working directory for {game.DisplayName}: raw={rawWorkdir}; warning={workDirWarning}");
            workingDirectory = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(launchTarget) ?? string.Empty;
        }

        logger.Info($"Launch contract raw: target={rawTarget}; args={contract.Arguments}; workdir={rawWorkdir}");
        logger.Info($"Launch contract resolved: target={launchTarget}; args={contract.Arguments}; workdir={workingDirectory}");

        var workDirExists = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory);
        var productSummary = string.Empty;
        if (launchTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(launchTarget);
                if (!string.IsNullOrWhiteSpace(info.ProductName) || !string.IsNullOrWhiteSpace(info.FileVersion))
                {
                    productSummary = $" ({info.ProductName} {info.FileVersion})".Trim();
                }
            }
            catch
            {
            }
        }

        progress?.Report(new LaunchOverlayUpdate
        {
            Stage = LaunchOverlayStage.Preflight,
            DisplayName = game.DisplayName,
            Message = workDirExists
                ? $"Preflight checks...{productSummary}"
                : $"Preflight checks... warning: working directory missing{productSummary}",
            TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
            RawTarget = rawTarget,
            RawArgs = rawArgs,
            RawWorkDir = rawWorkdir,
            ResolvedTarget = launchTarget,
            ResolvedArgs = rawArgs,
            ResolvedWorkDir = workingDirectory,
            TargetExists = true,
            WorkDirExists = workDirExists
        });

        game.Stats.LastPlayedUtc = DateTime.UtcNow.ToString("O");
        game.Stats.LastValidatedUtc = DateTime.UtcNow.ToString("O");
        game.Stats.LastResult = "OK";
        await registryService.SaveAsync(registry);

        if (!config.Launch.ActuallyLaunch)
        {
            logger.Info($"Would launch {game.DisplayName}: {launchTarget}");
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Started,
                DisplayName = game.DisplayName,
                Message = "Launching skipped (ActuallyLaunch=false).",
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ResolvedTarget = launchTarget,
                ResolvedArgs = rawArgs,
                ResolvedWorkDir = workingDirectory,
                TargetExists = true,
                WorkDirExists = workDirExists
            });
            return (int)ExitCodes.Success;
        }

        try
        {
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Launching,
                DisplayName = game.DisplayName,
                Message = "Launching...",
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ResolvedTarget = launchTarget,
                ResolvedArgs = rawArgs,
                ResolvedWorkDir = workingDirectory,
                TargetExists = true,
                WorkDirExists = workDirExists
            });

            var startInfo = new ProcessStartInfo
            {
                FileName = launchTarget,
                Arguments = contract.Arguments ?? string.Empty,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            logger.Info($"Launched {game.DisplayName} ({launchTarget})");

            int? earlyExitCode = null;
            if (process is not null)
            {
                using var earlyExitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(earlyExitCts.Token);
                    earlyExitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                }
            }

            var startedMessage = earlyExitCode.HasValue
                ? (earlyExitCode.Value == 0
                    ? "Running (process exited quickly with code 0)."
                    : $"Failed: process exited early ({earlyExitCode.Value}).")
                : "Running";
            var stage = earlyExitCode.HasValue && earlyExitCode.Value != 0
                ? LaunchOverlayStage.Failed
                : LaunchOverlayStage.Started;
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = stage,
                DisplayName = game.DisplayName,
                Message = startedMessage,
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ResolvedTarget = launchTarget,
                ResolvedArgs = rawArgs,
                ResolvedWorkDir = workingDirectory,
                TargetExists = true,
                WorkDirExists = workDirExists,
                ExitCode = earlyExitCode,
                ExceptionText = BuildWorkDirWarning(workDirWarning, workDirExists)
            });

            if (earlyExitCode.HasValue && earlyExitCode.Value != 0)
            {
                game.Stats.LastResult = "LaunchFailed";
                game.Stats.LastValidatedUtc = DateTime.UtcNow.ToString("O");
                await registryService.SaveAsync(registry);
                return (int)ExitCodes.LaunchFailed;
            }

            return (int)ExitCodes.Success;
        }
        catch (Exception ex)
        {
            game.Stats.LastResult = "LaunchFailed";
            game.Stats.LastValidatedUtc = DateTime.UtcNow.ToString("O");
            await registryService.SaveAsync(registry);

            logger.Error(ex.ToString());
            progress?.Report(new LaunchOverlayUpdate
            {
                Stage = LaunchOverlayStage.Failed,
                DisplayName = game.DisplayName,
                Message = "Failed",
                TokenSummary = BuildTokenSummary(rawTarget, rawWorkdir),
                RawTarget = rawTarget,
                RawArgs = rawArgs,
                RawWorkDir = rawWorkdir,
                ResolvedTarget = launchTarget,
                ResolvedArgs = rawArgs,
                ResolvedWorkDir = workingDirectory,
                TargetExists = true,
                WorkDirExists = Directory.Exists(workingDirectory),
                ExceptionText = ex.ToString()
            });
            return (int)ExitCodes.LaunchFailed;
        }
    }

    private static string BuildWorkDirWarning(string warning, bool workDirExists)
    {
        if (workDirExists)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(warning)
            ? "Working directory missing."
            : warning;
    }

    private static string BuildTokenSummary(string rawTarget, string rawWorkdir)
    {
        var combined = $"{rawTarget} {rawWorkdir}";
        var tokens = new List<string>();
        AddToken(tokens, combined, "{GamesRoot}");
        AddToken(tokens, combined, "{CacheRoot}");
        AddToken(tokens, combined, "{LaunchBoxRoot}");
        AddToken(tokens, combined, "{GameFolder}");
        AddToken(tokens, combined, "{MainExeDir}");
        AddToken(tokens, combined, "{RelayDir}");
        return tokens.Count == 0 ? "none" : string.Join(", ", tokens);
    }

    private static void AddToken(List<string> tokens, string value, string token)
    {
        if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(token);
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
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(root) &&
                !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }
        catch
        {
            return path.Trim();
        }
    }
}
