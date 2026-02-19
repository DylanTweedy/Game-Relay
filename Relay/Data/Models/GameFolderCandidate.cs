using System.IO;

namespace Relay.Data.Models;

public sealed class GameFolderCandidate
{
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public List<ExeCandidate> Exes { get; set; } = [];
    public string? SelectedMainExePath { get; set; }
    public HashSet<string> SelectedToolExePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int ValidExeCount { get; set; }
    public int ExcludedExeCount { get; set; }
    public int HiddenExeCount { get; set; }
    public string State { get; set; } = "New";
    public string Source { get; set; } = "Disk";
    public string SelectedMainExeFile =>
        string.IsNullOrWhiteSpace(SelectedMainExePath) ? string.Empty : Path.GetFileName(SelectedMainExePath);
}
