using System.IO;
using System.Text.Json;
using Relay.Data.Models;

namespace Relay.Services;

public sealed class ScanCacheService(LoggingService logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string CachePath = Path.Combine(BaseDirectory, "ScanCache.json");

    public async Task<ScanCache> LoadAsync()
    {
        if (!File.Exists(CachePath))
        {
            return new ScanCache();
        }

        await using var stream = File.OpenRead(CachePath);
        var model = await JsonSerializer.DeserializeAsync<ScanCache>(stream, JsonOptions);
        return model ?? new ScanCache();
    }

    public async Task SaveAsync(ScanCache cache)
    {
        await using var stream = File.Create(CachePath);
        await JsonSerializer.SerializeAsync(stream, cache, JsonOptions);
    }

    public Task ClearAsync()
    {
        if (File.Exists(CachePath))
        {
            File.Delete(CachePath);
            logger.Info("Deleted ScanCache.json");
        }

        return Task.CompletedTask;
    }

    public string GetCachePath() => CachePath;
}
