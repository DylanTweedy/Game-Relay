namespace Relay.Data.Models;

public enum LaunchOverlayStage
{
    Resolving,
    Preflight,
    Launching,
    Started,
    Failed,
    Exited
}

public sealed class LaunchOverlayUpdate
{
    public LaunchOverlayStage Stage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TokenSummary { get; set; } = string.Empty;
    public string RawTarget { get; set; } = string.Empty;
    public string RawArgs { get; set; } = string.Empty;
    public string RawWorkDir { get; set; } = string.Empty;
    public string ResolvedTarget { get; set; } = string.Empty;
    public string ResolvedArgs { get; set; } = string.Empty;
    public string ResolvedWorkDir { get; set; } = string.Empty;
    public bool? TargetExists { get; set; }
    public bool? WorkDirExists { get; set; }
    public string ExceptionText { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
}
