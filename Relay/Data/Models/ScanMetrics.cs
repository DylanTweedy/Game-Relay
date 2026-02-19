namespace Relay.Data.Models;

public sealed class ScanMetrics
{
    public int TotalFolders { get; set; }
    public int FoldersWithValidExe { get; set; }
    public int TotalExeCandidates { get; set; }
    public int MainSelected { get; set; }
    public int ToolsSelected { get; set; }
    public int Excluded { get; set; }
    public int Hidden { get; set; }
    public int CachedFolders { get; set; }
    public int SkippedKnownFolders { get; set; }
}
