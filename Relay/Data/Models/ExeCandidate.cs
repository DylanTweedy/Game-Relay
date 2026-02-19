using System.IO;

namespace Relay.Data.Models;

public sealed class ExeCandidate
{
    public string ExePath { get; set; } = string.Empty;
    public string ExeName => Path.GetFileName(ExePath);
    public string SuggestedName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = "MainCandidate";
    public string Reason { get; set; } = string.Empty;
    public bool IsToolSelected { get; set; }
    public bool IsMainOverride { get; set; }
    public string SizeMb => (SizeBytes / 1024d / 1024d).ToString("0.00");
}
