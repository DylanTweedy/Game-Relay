namespace Relay.Data.Models;

public sealed class LaunchContract
{
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool IsValid => !string.IsNullOrWhiteSpace(TargetPath);
}
