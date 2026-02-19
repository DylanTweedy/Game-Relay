namespace Relay.Data.Models;

public sealed class ScanCandidate
{
    public string ExePath { get; set; } = string.Empty;
    public string BaseFolder { get; set; } = string.Empty;
    public string SuggestedName { get; set; } = string.Empty;
    public long FileBytes { get; set; }
    public string Status { get; set; } = "New";
    public string Reason { get; set; } = string.Empty;
}
