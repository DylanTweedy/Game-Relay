namespace Relay.Data.Models;

public sealed class GameEntry
{
    public Guid GameKey { get; set; }
    public string LaunchKey { get; set; } = string.Empty;
    public bool ImportedFromShortcut { get; set; }
    public string ImportedShortcutIconLocation { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public InstallInfo Install { get; set; } = new();
    public LaunchSettings Launch { get; set; } = new();
    public StatsInfo Stats { get; set; } = new();
    public LaunchBoxLink? LaunchBoxLink { get; set; }
}
