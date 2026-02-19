namespace Relay.Data.Models;

public sealed class StatsInfo
{
    public long EstimatedBytes { get; set; }
    public string LastPlayedUtc { get; set; } = string.Empty;
    public string LastValidatedUtc { get; set; } = string.Empty;
    public string LastResult { get; set; } = "Unknown";
}
