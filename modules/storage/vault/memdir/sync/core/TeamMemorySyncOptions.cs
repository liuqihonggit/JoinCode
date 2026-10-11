
namespace Memdir.Sync;

/// <summary>
/// 团队记忆同步配置选项 — 描述本地/远程存储路径、同步间隔、冲突处理策略与文件监控等参数。
/// </summary>
public sealed class TeamMemorySyncOptions {
    /// <summary>配置节名称。</summary>
    public const string SectionName = "TeamMemorySync";

    /// <summary>本地监控目录路径。</summary>
    public string WatchPath { get; set; } = string.Empty;
    /// <summary>远程存储目录路径。</summary>
    public string RemoteStoragePath { get; set; } = string.Empty;
    /// <summary>自动同步间隔,默认 30 秒。</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>冲突检测时间窗口,默认 5 秒。本地与远程修改时间差在此窗口内视为冲突。</summary>
    public TimeSpan ConflictDetectionWindow { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>默认冲突解决策略,默认保留最新版本。</summary>
    public SyncConflictResolution DefaultConflictResolution { get; set; } = SyncConflictResolution.KeepNewest;
    /// <summary>是否启用自动同步,默认启用。</summary>
    public bool EnableAutoSync { get; set; } = true;
    /// <summary>是否启用文件监控,默认启用。</summary>
    public bool EnableFileWatching { get; set; } = true;
    /// <summary>监控的文件模式列表,默认包含 md/json/txt。</summary>
    public List<string> FilePatterns { get; set; } = new() { "*.md", "*.json", "*.txt" };
}