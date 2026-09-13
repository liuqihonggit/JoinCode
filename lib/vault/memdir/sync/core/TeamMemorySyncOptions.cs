
namespace Memdir.Sync;

public sealed class TeamMemorySyncOptions
{
    public const string SectionName = "TeamMemorySync";

    public string WatchPath { get; set; } = string.Empty;
    public string RemoteStoragePath { get; set; } = string.Empty;
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConflictDetectionWindow { get; set; } = TimeSpan.FromSeconds(5);
    public SyncConflictResolution DefaultConflictResolution { get; set; } = SyncConflictResolution.KeepNewest;
    public bool EnableAutoSync { get; set; } = true;
    public bool EnableFileWatching { get; set; } = true;
    public List<string> FilePatterns { get; set; } = new() { "*.md", "*.json", "*.txt" };
}
