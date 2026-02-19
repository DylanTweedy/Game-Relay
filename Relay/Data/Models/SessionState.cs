namespace Relay.Data.Models;

public sealed class SessionState
{
    public int SchemaVersion { get; set; } = 1;
    public string SavedUtc { get; set; } = string.Empty;
    public List<ShortcutScanResult> ShortcutResults { get; set; } = [];
    public List<GameFolderCandidate> FolderCandidates { get; set; } = [];
    public Dictionary<string, LaunchContract> LaunchMainByGameKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ScannerStatusText { get; set; } = string.Empty;
    public string ShortcutsStatusText { get; set; } = string.Empty;
}
