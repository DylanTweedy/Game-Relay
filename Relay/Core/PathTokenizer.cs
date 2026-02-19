using System.IO;
using Relay.Data;
using Relay.Data.Models;

namespace Relay.Core;

public static class PathTokenizer
{
    public static string TokenizeAbsolutePath(string absolute, InstallInfo install, Config config, string relayDir)
    {
        if (string.IsNullOrWhiteSpace(absolute))
        {
            return string.Empty;
        }

        var value = PathCanonicalizer.NormalizeSeparators(absolute.Trim());
        if (!Path.IsPathRooted(value))
        {
            return PathCanonicalizer.NormalizeTokenPath(value);
        }

        var normalized = NormalizePath(value);
        var gamesRoot = NormalizePath(config.Paths.GamesRoot);
        var cacheRoot = NormalizePath(config.Paths.CacheRoot);
        var launchBoxRaw = !string.IsNullOrWhiteSpace(config.Paths.LaunchBoxRoot)
            ? config.Paths.LaunchBoxRoot
            : config.LaunchBox.RootPath;
        var launchBoxRoot = NormalizePath(launchBoxRaw);
        var gameFolder = NormalizePath(GetGameFolder(install));
        var normalizedRelay = NormalizePath(relayDir);

        var tokenized = TryTokenizeInsideRoot(normalized, gamesRoot, "{GamesRoot}")
                        ?? TryTokenizeInsideRoot(normalized, cacheRoot, "{CacheRoot}")
                        ?? TryTokenizeInsideRoot(normalized, launchBoxRoot, "{LaunchBoxRoot}")
                        ?? TryTokenizeInsideRoot(normalized, gameFolder, "{GameFolder}")
                        ?? TryTokenizeInsideRoot(normalized, normalizedRelay, "{RelayDir}");

        return tokenized ?? PathCanonicalizer.NormalizeTokenPath(normalized);
    }

    public static string TokenizeForStorage(string rawPathOrTokenized, InstallInfo install, Config config, string relayDir)
    {
        if (string.IsNullOrWhiteSpace(rawPathOrTokenized))
        {
            return string.Empty;
        }

        var value = PathCanonicalizer.NormalizeSeparators(rawPathOrTokenized.Trim());
        if (!Path.IsPathRooted(value))
        {
            return PathCanonicalizer.NormalizeTokenPath(value);
        }

        return TokenizeAbsolutePath(value, install, config, relayDir);
    }

    private static string? TryTokenizeInsideRoot(string normalizedPath, string normalizedRoot, string token)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return null;
        }

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return token;
        }

        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = normalizedPath[normalizedRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return $"{token}\\{relative}";
    }

    private static string GetGameFolder(InstallInfo install)
    {
        return !string.IsNullOrWhiteSpace(install.GameFolderPath)
            ? install.GameFolderPath
            : install.BaseFolder;
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
