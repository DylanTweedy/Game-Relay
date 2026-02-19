using Relay.Data;
using Relay.Services;

namespace Relay.Core;

public static class Validator
{
    public static bool ValidateConfig(Config config, LoggingService? logger = null)
    {
        if (config.SchemaVersion < 1)
        {
            return false;
        }

        if (config.Cache.Enabled && string.IsNullOrWhiteSpace(config.Paths.CacheRoot))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Paths.ShortcutOutputRoot))
        {
            logger?.Warn("Paths.ShortcutOutputRoot is empty.");
        }

        return true;
    }
}
