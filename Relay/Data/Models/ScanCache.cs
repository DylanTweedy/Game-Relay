namespace Relay.Data.Models;

public sealed class ScanCache
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> ScanRootsSnapshot { get; set; } = [];
    public Dictionary<string, FolderCacheEntry> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FolderCacheEntry
{
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string LastScannedUtc { get; set; } = string.Empty;
    public string FolderFingerprint { get; set; } = string.Empty;
    public string? SelectedMainExePath { get; set; }
    public List<ExeCandidate> ExeCandidates { get; set; } = [];
}
