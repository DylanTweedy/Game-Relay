using System.IO;
using Relay.Data;
using Relay.Data.Models;

namespace Relay.Core;

public static class PathTokenResolver
{
    public static bool TryResolve(
        string raw,
        InstallInfo install,
        LaunchContract contract,
        Config config,
        string relayDir,
        out string resolved,
        out string warning)
    {
        resolved = string.Empty;
        warning = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var gameFolder = GetGameFolder(install);
        var cacheRoot = NormalizePath(config.Paths.CacheRoot);
        var gamesRoot = NormalizePath(config.Paths.GamesRoot);
        var launchBoxRaw = !string.IsNullOrWhiteSpace(config.Paths.LaunchBoxRoot)
            ? config.Paths.LaunchBoxRoot
            : config.LaunchBox.RootPath;
        var launchBoxRoot = NormalizePath(launchBoxRaw);
        var normalizedRelay = NormalizePath(relayDir);
        var text = PathCanonicalizer.NormalizeSeparators(raw.Trim());
        text = ReplaceTokenIfValue(text, "{GamesRoot}", gamesRoot);
        text = ReplaceTokenIfValue(text, "{CacheRoot}", cacheRoot);
        text = ReplaceTokenIfValue(text, "{LaunchBoxRoot}", launchBoxRoot);
        text = ReplaceTokenIfValue(text, "{RelayDir}", normalizedRelay);
        text = ReplaceTokenIfValue(text, "{GameFolder}", gameFolder);

        if (text.Contains("{MainExeDir}", StringComparison.OrdinalIgnoreCase))
        {
            var mainExeDir = ResolveMainExeDir(install, contract, config, normalizedRelay);
            text = text.Replace("{MainExeDir}", mainExeDir, StringComparison.OrdinalIgnoreCase);
        }

        if (Path.IsPathRooted(text))
        {
            resolved = NormalizePath(text);
            return true;
        }

        if (text.Contains('{'))
        {
            warning = $"Path contains unresolved token(s): {raw}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            warning = $"Relative path requires {{GameFolder}} but it is empty: {raw}";
            return false;
        }

        resolved = NormalizePath(Path.Combine(gameFolder, text));
        return true;
    }

    public static string Resolve(string raw, InstallInfo install, LaunchContract contract, Config config, string relayDir)
    {
        return TryResolve(raw, install, contract, config, relayDir, out var resolved, out _)
            ? resolved
            : string.Empty;
    }

    private static string ResolveMainExeDir(InstallInfo install, LaunchContract contract, Config config, string relayDir)
    {
        var rawTarget = contract?.TargetPath ?? install.ExePath;
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            return GetGameFolder(install);
        }

        var targetNoMainToken = PathCanonicalizer.NormalizeSeparators(rawTarget)
            .Replace("{MainExeDir}", string.Empty, StringComparison.OrdinalIgnoreCase);
        var resolvedMain = Resolve(targetNoMainToken, install, new LaunchContract
        {
            TargetPath = targetNoMainToken,
            Arguments = contract?.Arguments ?? string.Empty,
            WorkingDirectory = contract?.WorkingDirectory ?? string.Empty
        }, config, relayDir);

        var mainDir = Path.GetDirectoryName(resolvedMain) ?? string.Empty;
        return NormalizePath(mainDir);
    }

    private static string GetGameFolder(InstallInfo install)
    {
        var folder = !string.IsNullOrWhiteSpace(install.GameFolderPath)
            ? install.GameFolderPath
            : install.BaseFolder;
        return NormalizePath(folder);
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

    private static string ReplaceTokenIfValue(string text, string token, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return text;
        }

        return text.Replace(token, value, StringComparison.OrdinalIgnoreCase);
    }
}
