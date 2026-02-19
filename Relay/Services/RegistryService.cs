using System.IO;
using System.Text.Json;
using Relay.Data;

namespace Relay.Services;

public sealed class RegistryService(LoggingService logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private string RegistryPath => Path.Combine(BaseDirectory, "Registry.json");

    public async Task EnsureExistsAsync()
    {
        if (File.Exists(RegistryPath))
        {
            return;
        }

        var model = new Registry();
        await SaveAsync(model);
        logger.Info("Created default Registry.json");
    }

    public async Task<Registry> LoadAsync()
    {
        await EnsureExistsAsync();
        await using var stream = File.OpenRead(RegistryPath);
        var registry = await JsonSerializer.DeserializeAsync<Registry>(stream, JsonOptions);
        return registry ?? new Registry();
    }

    public async Task SaveAsync(Registry registry)
    {
        EnsureBackup();
        await using var stream = File.Create(RegistryPath);
        await JsonSerializer.SerializeAsync(stream, registry, JsonOptions);
    }

    private void EnsureBackup()
    {
        if (!File.Exists(RegistryPath))
        {
            return;
        }

        var backupPath = RegistryPath + ".bak";
        File.Copy(RegistryPath, backupPath, overwrite: true);
    }
}
