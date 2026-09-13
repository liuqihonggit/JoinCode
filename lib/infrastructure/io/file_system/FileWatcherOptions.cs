namespace IO.FileSystem;

/// <summary>
/// 文件监控统一配置 — 所有文件监控 Actor 的防抖间隔集中配置。
/// <para>对齐 ADR 0101: 消除散落的硬编码防抖间隔(200ms/500ms/1s/5s),改为集中配置。</para>
/// <para>热重载: 可通过 settings.json 调整,运行时生效(对齐 ADR 0015 双变量切换)。</para>
/// </summary>
public sealed class FileWatcherOptions
{
    /// <summary>配置热重载防抖(ConfigChangeNotifierActor) — 默认 500ms</summary>
    public TimeSpan ConfigDebounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>代码索引增量防抖(IndexUpdateActor) — 默认 500ms</summary>
    public TimeSpan IndexDebounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>插件热重载防抖(PluginHotReloadActor) — 默认 500ms</summary>
    public TimeSpan PluginDebounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>技能发现防抖(SkillDiscoveryActor) — 默认 500ms</summary>
    public TimeSpan SkillDebounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>关键词热加载防抖(DynamicKeywordConfigActor) — 默认 200ms</summary>
    public TimeSpan KeywordDebounce { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Cron 任务防抖(FileCronTaskStore) — 默认 300ms(新增,原无防抖)</summary>
    public TimeSpan CronDebounce { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>团队同步防抖(TeamMemorySyncService) — 默认 300ms(新增,原无防抖)</summary>
    public TimeSpan SyncDebounce { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>内部写入过滤窗口 — 窗口内的变更视为自身写入回声,丢弃 — 默认 5s</summary>
    public TimeSpan InternalWriteWindow { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Actor 邮箱容量 — 满时丢弃最旧命令 — 默认 1000</summary>
    public int ActorMailboxCapacity { get; init; } = 1000;
}
