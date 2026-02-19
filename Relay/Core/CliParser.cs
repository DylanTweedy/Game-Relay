namespace Relay.Core;

public enum CliCommand
{
    None,
    Launch,
    Manager,
    Validate
}

public sealed record CliParseResult(CliCommand Command, Guid? GameKey, string? ToolPath, bool? OverlayEnabled);

public static class CliParser
{
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliParseResult(CliCommand.Manager, null, null, null);
        }

        if (string.Equals(args[0], "manager", StringComparison.OrdinalIgnoreCase))
        {
            return new CliParseResult(CliCommand.Manager, null, null, null);
        }

        if (string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
        {
            return new CliParseResult(CliCommand.Validate, null, null, null);
        }

        if (string.Equals(args[0], "launch", StringComparison.OrdinalIgnoreCase))
        {
            var (key, tool, overlayEnabled) = ParseLaunchOptions(args.Skip(1).ToArray());
            return new CliParseResult(CliCommand.Launch, key, tool, overlayEnabled);
        }

        var (implicitKey, implicitTool, implicitOverlayEnabled) = ParseLaunchOptions(args);
        if (implicitKey is not null)
        {
            return new CliParseResult(CliCommand.Launch, implicitKey, implicitTool, implicitOverlayEnabled);
        }

        return new CliParseResult(CliCommand.None, null, null, null);
    }

    private static (Guid? Key, string? ToolPath, bool? OverlayEnabled) ParseLaunchOptions(string[] args)
    {
        Guid? key = null;
        string? toolPath = null;
        bool? overlayEnabled = null;

        if (args.Length == 1 && Guid.TryParse(args[0], out var directGuid))
        {
            key = directGuid;
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--key", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                Guid.TryParse(args[i + 1], out var parsedGuid))
            {
                key = parsedGuid;
                i++;
                continue;
            }

            if (string.Equals(args[i], "--tool", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                toolPath = args[i + 1];
                i++;
                continue;
            }

            if (string.Equals(args[i], "--overlay", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (bool.TryParse(args[i + 1], out var parsedOverlayEnabled))
                {
                    overlayEnabled = parsedOverlayEnabled;
                }

                i++;
            }
        }

        return (key, toolPath, overlayEnabled);
    }
}
