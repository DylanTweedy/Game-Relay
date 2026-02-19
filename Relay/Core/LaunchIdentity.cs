using Relay.Data;
using Relay.Data.Models;

namespace Relay.Core;

public static class LaunchIdentity
{
    public static string BuildLaunchKey(string targetPath, string arguments, string workingDirectory)
    {
        var identity = PathCanonicalizer.BuildIdentityString(targetPath, arguments, workingDirectory);
        return PathCanonicalizer.ComputeSha256Hex(identity);
    }

    public static bool IsMatch(string leftKey, string rightKey)
    {
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
        {
            return false;
        }

        return string.Equals(leftKey.Trim(), rightKey.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeArguments(string arguments)
    {
        return PathCanonicalizer.NormalizeArguments(arguments);
    }

    public static bool PreferExePathFallback(string arguments)
    {
        return string.IsNullOrWhiteSpace(NormalizeArguments(arguments));
    }

    public static string BuildLaunchKeyForGame(GameEntry game, Config config, string relayDir)
    {
        var contract = game.Launch.Main ?? new LaunchContract();
        var rawTarget = !string.IsNullOrWhiteSpace(contract.TargetPath)
            ? contract.TargetPath
            : game.Install.ExePath;
        var rawWorkDir = !string.IsNullOrWhiteSpace(contract.WorkingDirectory)
            ? contract.WorkingDirectory
            : game.Install.WorkingDir;
        var args = contract.Arguments ?? game.Install.Args ?? string.Empty;

        var tokenTarget = PathTokenizer.TokenizeForStorage(rawTarget, game.Install, config, relayDir);
        var tokenWorkdir = PathTokenizer.TokenizeForStorage(rawWorkDir, game.Install, config, relayDir);
        return BuildLaunchKey(tokenTarget, args, tokenWorkdir);
    }
}
