namespace Core.Configuration;

/// <summary>
/// 一步配置迁移 —— 每条迁移有唯一递增的 <see cref="Version"/>，只在用户数据落后时跑一次。
/// <para><b>为什么要有版本号</b>：TASK089 的手写迁移靠「结构幂等」（搬完就没得搬）自我保护，
/// 这对搬键有效，但对「改值、合并字段、重写格式」这类迁移不成立 —— 它们无法用「还有没有可搬项」
/// 判断是否跑过，只能靠外部记录。版本号就是这份记录，落在 <c>~/.jcc/config-migrations.json</c>。</para>
/// <para><b>一个迁移一个文件</b>：放在 <c>core/loading/core/migrations/</c> 下，按 <c>V{n}</c> 前缀命名。
/// 新增迁移 = 加一个类 + 在 <see cref="ConfigMigrationRunner.Migrations"/> 里登记一行（显式登记而非反射扫描，
/// 契约层与配置层都要 AOT 安全，且迁移顺序必须肉眼可审）。</para>
/// <para><b>幂等要求</b>：即使版本标记丢失（用户删了文件、从备份恢复旧目录），迁移也会重跑；
/// 所以每一步仍应写成「重跑一次也没坏处」。版本号只优化「不重复跑」，不是正确性的唯一依赖。</para>
/// </summary>
public interface IConfigMigration {
    /// <summary>迁移版本号 —— 从 1 起严格递增，已发布后不得改写（改了就变成另一条迁移）。</summary>
    int Version { get; }

    /// <summary>迁移简称 —— 只用于日志与版本标记里的可读记录。</summary>
    string Name { get; }

    /// <summary>
    /// 执行迁移。失败时直接抛出由 <see cref="ConfigMigrationRunner"/> 统一记录并停止推进版本，
    /// 不要在这里吞异常 —— 吞掉会让「失败」和「没什么可做」长得一样，用户永远等不到重试。
    /// </summary>
    /// <param name="fs">文件系统抽象（生产用 PhysicalFileSystem，探针可注入）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>迁移结果 — 搬动了什么，供日志与报告使用</returns>
    Task<ConfigMigrationOutcome> ApplyAsync(IFileSystem fs, ILogger? logger, CancellationToken cancellationToken);
}

/// <summary>单步迁移的结果 — 让调用方能说清「到底动没动数据」。</summary>
/// <param name="MovedKeys">被搬动的键</param>
/// <param name="ConflictedKeys">两边都有值、按规则保留一侧的键</param>
public readonly record struct ConfigMigrationOutcome(IReadOnlyList<string> MovedKeys, IReadOnlyList<string> ConflictedKeys) {
    /// <summary>没有需要迁移的内容。</summary>
    public static ConfigMigrationOutcome None { get; } = new([], []);

    /// <summary>本次是否真的改了磁盘。</summary>
    public bool Changed => MovedKeys.Count > 0 || ConflictedKeys.Count > 0;
}

/// <summary>迁移版本标记 — 落盘为 <c>~/.jcc/config-migrations.json</c>。</summary>
public sealed class ConfigMigrationState {
    /// <summary>已应用到的最高版本号；0 表示从未迁移过。</summary>
    [JsonPropertyName("appliedVersion")]
    public int AppliedVersion { get; init; }

    /// <summary>最近若干次迁移记录（保留尾部，防止无限增长）。</summary>
    [JsonPropertyName("history")]
    public List<ConfigMigrationRecord> History { get; init; } = [];
}

/// <summary>单条迁移记录 — 写进版本标记，用于事后回答「这版数据是怎么变成现在这样的」。</summary>
public sealed class ConfigMigrationRecord {
    /// <summary>迁移版本号。</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>迁移简称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>执行时间（UTC）。</summary>
    [JsonPropertyName("appliedAtUtc")]
    public DateTimeOffset AppliedAtUtc { get; init; }

    /// <summary>本次迁移的简述（如搬动的键名）。</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

/// <summary>一次迁移运行的汇总 — 供调用方记日志。</summary>
/// <param name="Applied">本次实际执行的迁移</param>
/// <param name="FromVersion">运行前的版本</param>
/// <param name="ToVersion">运行后的版本（失败时等于 <paramref name="FromVersion"/>）</param>
/// <param name="FailedVersion">失败的迁移版本；全部成功为 <c>null</c></param>
public readonly record struct ConfigMigrationReport(
    IReadOnlyList<IConfigMigration> Applied,
    int FromVersion,
    int ToVersion,
    int? FailedVersion) {
    /// <summary>没有迁移需要执行（版本已是最新或没有任何迁移）。</summary>
    public static ConfigMigrationReport Nothing { get; } = new([], 0, 0, null);

    /// <summary>是否有迁移失败 —— 下次启动会从该版本重试。</summary>
    public bool Failed => FailedVersion is not null;
}
