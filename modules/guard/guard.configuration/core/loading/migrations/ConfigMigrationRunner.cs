namespace Core.Configuration;

/// <summary>
/// 配置迁移执行器 —— 按版本号顺序把用户数据升到当前格式，并把进度记在
/// <c>~/.jcc/config-migrations.json</c>（见 <see cref="IConfigMigration"/>）。
/// <para><b>为什么需要它</b>：迁移逻辑原本是一个手写方法 <c>SettingsSourceMigration.RunAsync</c>，
/// 每次配置读写前都跑一遍，靠「结构幂等」自保。这条约定只对「搬键」成立：一旦要改值、合并字段、
/// 换存储格式，就无法再靠「还有没有可搬项」判断跑过没有，于是只能加版本号 —— 与其每条迁移各自
/// 发明一套标记，不如把「读版本 → 依次执行 → 每步落盘 → 失败停在这里」收进这一个地方。</para>
/// <para><b>失败策略</b>：某一步抛异常就停下，<b>不推进版本</b>，下次启动从这一步重试；
/// 已完成的步骤不会重跑（版本已经落盘）。单个迁移失败不影响配置读写可用性 —— 调用方拿到的是一份报告，
/// 而不是异常。</para>
/// <para><b>并发</b>：CLI 与 GUI 可能同时启动。版本标记的写入是「后写覆盖」，最坏情况是同一步被两个进程
/// 各跑一次 —— 所以每条迁移本身仍必须幂等。</para>
/// </summary>
public static class ConfigMigrationRunner {
    /// <summary>已登记的迁移（严格按 <see cref="IConfigMigration.Version"/> 升序执行）。</summary>
    /// <remarks>
    /// 显式登记而非反射扫描：迁移顺序是数据正确性的一部分，必须肉眼可审；
    /// 反射在 AOT 下也不可靠。新增一步 = 在这里加一行。
    /// </remarks>
    public static IReadOnlyList<IConfigMigration> Migrations { get; } = [
        // v1: TASK089 —— settings.json 里的 global 键搬到 global.json
        new SettingsSourceMigration(),
    ];

    /// <summary>版本标记里最多保留的记录条数 —— 只留最近几步，避免文件随时间无限增长。</summary>
    private const int HistoryLimit = 10;

    /// <summary>当前代码支持的最高迁移版本；没有迁移时为 0。</summary>
    public static int LatestVersion => Migrations.Count == 0 ? 0 : Migrations.Max(m => m.Version);

    /// <summary>
    /// 把用户数据升到当前版本。已是最新时不做任何磁盘读以外的动作。
    /// </summary>
    /// <param name="fs">文件系统抽象（生产用 PhysicalFileSystem，探针可注入）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行报告 — 失败时 <see cref="ConfigMigrationReport.FailedVersion"/> 指出停在哪一步</returns>
    public static Task<ConfigMigrationReport> RunAsync(
        IFileSystem fs, ILogger? logger = null, CancellationToken cancellationToken = default)
        => RunAsync(fs, Migrations, logger, cancellationToken);

    /// <summary>
    /// 用指定迁移集合执行 —— 生产走 <see cref="Migrations"/>，测试用它验证版本推进与失败停驻。
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="migrations">要执行的迁移集合（本次按版本号排序）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行报告</returns>
    public static async Task<ConfigMigrationReport> RunAsync(
        IFileSystem fs,
        IReadOnlyList<IConfigMigration> migrations,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) {
        var ordered = migrations.OrderBy(m => m.Version).ToArray();
        if (ordered.Length == 0)
            return ConfigMigrationReport.Nothing;

        var markerPath = AppDataConstants.Paths.ConfigMigrationsFilePath;
        var state = await ReadStateAsync(markerPath, fs, logger, cancellationToken).ConfigureAwait(false);
        var from = state.AppliedVersion;

        if (from >= ordered[^1].Version)
            return new ConfigMigrationReport([], from, from, null);

        var applied = new List<IConfigMigration>(ordered.Length);
        var current = from;

        foreach (var migration in ordered) {
            if (migration.Version <= current)
                continue;

            ConfigMigrationOutcome outcome;
            try {
                outcome = await migration.ApplyAsync(fs, logger, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                // 不推进版本 —— 下次启动会从这一步重试。已完成的步骤不会重跑。
                logger?.LogWarning(
                    ex,
                    "配置迁移 v{Version}（{Name}）失败，停留在 v{Current}，下次启动重试",
                    migration.Version,
                    migration.Name,
                    current);
                return new ConfigMigrationReport(applied, from, current, migration.Version);
            }

            current = migration.Version;
            applied.Add(migration);
            state = AppendHistory(state, new ConfigMigrationRecord {
                Version = migration.Version,
                Name = migration.Name,
                AppliedAtUtc = DateTimeOffset.UtcNow,
                Detail = DescribeOutcome(outcome),
            }, current);

            // 每步落盘：中途被杀掉时，已完成的步骤不会因为「标记没写过」而重跑。
            await WriteStateAsync(markerPath, state, fs, logger, cancellationToken).ConfigureAwait(false);
        }

        if (applied.Count > 0) {
            logger?.LogInformation(
                "配置迁移完成：v{From} → v{To}（{Names}）",
                from,
                current,
                string.Join(", ", applied.Select(m => m.Name)));
        }

        return new ConfigMigrationReport(applied, from, current, null);
    }

    private static string? DescribeOutcome(ConfigMigrationOutcome outcome) {
        if (!outcome.Changed)
            return "无需变动";
        if (outcome.MovedKeys.Count == 0)
            return $"保留既有值: {string.Join(", ", outcome.ConflictedKeys)}";
        return outcome.ConflictedKeys.Count == 0
            ? $"搬运: {string.Join(", ", outcome.MovedKeys)}"
            : $"搬运: {string.Join(", ", outcome.MovedKeys)}; 保留既有值: {string.Join(", ", outcome.ConflictedKeys)}";
    }

    private static ConfigMigrationState AppendHistory(ConfigMigrationState state, ConfigMigrationRecord record, int appliedVersion) {
        var history = new List<ConfigMigrationRecord>(state.History) { record };
        if (history.Count > HistoryLimit)
            history.RemoveRange(0, history.Count - HistoryLimit);

        return new ConfigMigrationState { AppliedVersion = appliedVersion, History = history };
    }

    /// <summary>读版本标记；文件不存在或损坏一律按「从未迁移」处理（迁移幂等，重跑无害）。</summary>
    private static async Task<ConfigMigrationState> ReadStateAsync(
        string markerPath, IFileSystem fs, ILogger? logger, CancellationToken cancellationToken) {
        if (!fs.FileExists(markerPath))
            return new ConfigMigrationState();

        try {
            var text = await fs.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return new ConfigMigrationState();
            return JsonSerializer.Deserialize(text, ConfigIndentedJsonContext.Default.ConfigMigrationState)
                ?? new ConfigMigrationState();
        } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
            // 标记坏了不是灾难：迁移本身幂等，当作 v0 重跑一遍即可。
            logger?.LogWarning(ex, "配置迁移版本标记不可读（{Path}），按从未迁移处理", markerPath);
            return new ConfigMigrationState();
        }
    }

    /// <summary>写版本标记 —— 写失败只记日志：本次迁移已经生效，下次启动重跑一遍而已。</summary>
    private static async Task WriteStateAsync(
        string markerPath, ConfigMigrationState state, IFileSystem fs, ILogger? logger, CancellationToken cancellationToken) {
        try {
            DirectoryHelper.EnsureDirectoryExists(fs, Path.GetDirectoryName(markerPath));
            // 走宽松序列化：默认编码器会把 detail 里的中文转成 \uXXXX，用户打开这个文件看不懂发生了什么
            var text = RelaxedJsonSerializer.Serialize(state, ConfigIndentedJsonContext.Default);
            await fs.WriteAllTextAsync(markerPath, text, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogWarning(ex, "配置迁移版本标记写入失败（{Path}），下次启动会重跑已完成的迁移", markerPath);
        }
    }
}
