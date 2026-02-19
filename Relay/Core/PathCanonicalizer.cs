using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Relay.Core;

public static class PathCanonicalizer
{
    public static string NormalizeTokenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var value = NormalizeSeparators(path.Trim());
        if (Path.IsPathRooted(value))
        {
            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
            }
        }

        value = CanonicalizeKnownTokens(value);

        if (value.Length > 3 && value.EndsWith('\\'))
        {
            value = value.TrimEnd('\\');
        }

        return value.Trim();
    }

    public static string NormalizeArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(arguments.Length);
        var inQuotes = false;
        var previousSpace = false;
        foreach (var ch in arguments.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(ch);
                previousSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (!previousSpace)
                {
                    sb.Append(' ');
                    previousSpace = true;
                }

                continue;
            }

            sb.Append(ch);
            previousSpace = false;
        }

        return sb.ToString().Trim();
    }

    public static string BuildIdentityString(string targetPath, string arguments, string workingDirectory)
    {
        var canonicalTarget = NormalizeTokenPath(targetPath);
        var canonicalArgs = NormalizeArguments(arguments);
        var canonicalWorkDir = NormalizeTokenPath(workingDirectory);
        return $"target={canonicalTarget}|args={canonicalArgs}|workdir={canonicalWorkDir}";
    }

    public static string ComputeSha256Hex(string value)
    {
        var input = value ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CanonicalizeKnownTokens(string text)
    {
        var value = text;
        value = value.Replace("{gamesroot}", "{GamesRoot}", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{cacheroot}", "{CacheRoot}", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{launchboxroot}", "{LaunchBoxRoot}", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{gamefolder}", "{GameFolder}", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{mainexedir}", "{MainExeDir}", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("{relaydir}", "{RelayDir}", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    public static string NormalizeSeparators(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace('/', '\\');
    }
}
