namespace Relay.Data.Models;

public sealed class LaunchSettings
{
    public string PreferredMode { get; set; } = "Cache";
    public bool AllowOverlay { get; set; } = true;
    public bool Pinned { get; set; }
    public LaunchContract Main { get; set; } = new();
    public Dictionary<string, LaunchContract> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
