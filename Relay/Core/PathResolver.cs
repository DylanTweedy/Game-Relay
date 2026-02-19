using System.IO;

namespace Relay.Core;

public static class PathResolver
{
    public static string Resolve(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(pathOrRelative))
        {
            return Path.GetFullPath(pathOrRelative);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, pathOrRelative));
    }
}
