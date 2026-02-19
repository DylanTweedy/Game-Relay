namespace Relay.Data;

public sealed class Config
{
    public int SchemaVersion { get; set; } = 1;
    public LaunchBoxConfig LaunchBox { get; set; } = new();
    public PathsConfig Paths { get; set; } = new();
    public LaunchConfig Launch { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public ScanningConfig Scanning { get; set; } = new();
    public ScannerRulesConfig ScannerRules { get; set; } = new();
    public DiagnosticsConfig Diagnostics { get; set; } = new();
}

public sealed class LaunchBoxConfig
{
    public string RootPath { get; set; } = string.Empty;
    public List<string> PreferredPlatforms { get; set; } = [];
}

public sealed class PathsConfig
{
    public string ShortcutImportRoot { get; set; } = ".\\ShortcutImport";
    public string ShortcutOutputRoot { get; set; } = string.Empty;
    public string GamesRoot { get; set; } = string.Empty;
    public string CacheRoot { get; set; } = string.Empty;
    public string LaunchBoxRoot { get; set; } = string.Empty;
    public string TempRoot { get; set; } = string.Empty;
    public List<string> ScanRoots { get; set; } = [];
}

public sealed class LaunchConfig
{
    public bool ActuallyLaunch { get; set; } = true;
}

public sealed class CacheConfig
{
    public bool Enabled { get; set; } = true;
    public long MaxBytes { get; set; }
    public string PurgePolicy { get; set; } = "LRU";
    public bool KeepPinned { get; set; } = true;
}

public sealed class OverlayConfig
{
    public bool Enabled { get; set; } = true;
    public int FadeOutMs { get; set; } = 300;
    public int AutoCloseSeconds { get; set; } = 3;
    public bool ShowDetails { get; set; } = true;
}

public sealed class ScanningConfig
{
    public bool Enabled { get; set; } = true;
    public long MinExeBytes { get; set; } = 524288;
    public List<string> ExcludedExePatterns { get; set; } = [];
    public List<string> ExcludedFolderNames { get; set; } = [];
}

public sealed class ScannerRulesConfig
{
    public List<string> ExeIgnoreNamePatterns { get; set; } = [];
    public List<string> ExeIgnoreFolderNamePatterns { get; set; } = [];
}

public sealed class DiagnosticsConfig
{
    public bool VerboseLogging { get; set; }
}
