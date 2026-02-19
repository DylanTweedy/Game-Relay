using Relay.Data.Models;

namespace Relay.Data;

public sealed class Registry
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> HiddenExecutables { get; set; } = [];
    public List<GameEntry> Games { get; set; } = [];
}
