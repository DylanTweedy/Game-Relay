namespace Relay.Data.Models;

public sealed class ShortcutScanResult
{
    public string ShortcutPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconLocation { get; set; } = string.Empty;
    public bool TargetExists { get; set; }
    public string Kind { get; set; } = "Unknown";
    public string Status { get; set; } = "Unknown";
    public string LaunchKey { get; set; } = string.Empty;
    public string MappedGameFolderPath { get; set; } = string.Empty;
}
