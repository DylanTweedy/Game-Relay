namespace Relay.Data.Models;

public sealed class InstallInfo
{
    public string GameFolderPath { get; set; } = string.Empty;
    public string BaseFolder { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public List<string> ToolExePaths { get; set; } = [];
    public string Args { get; set; } = string.Empty;
    public string WorkingDir { get; set; } = string.Empty;
}
