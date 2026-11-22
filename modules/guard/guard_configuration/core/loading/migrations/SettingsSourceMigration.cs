namespace Core.Configuration;

/// <summary>
/// v1 — 配置存储源迁移：把历史误写在 <c>settings.json</c> 里的 global 键搬到 <c>global.json</c>。
/// <para><b>为什么需要</b>：TASK089 之前，<c>/theme</c>、GUI 把主题等 10 个键写进 <c>settings.json</c> 的
/// <c>current</c> 分支，而 <see cref="JoinCode.Abstractions.Configuration.Settings.ConfigKeyRegistry"/>
/// 声明它们属于 <c>global.json</c>（对齐 TS <c>~/.claude.json</c>）。
/// 收敛存储源之后，旧位置的值会「消失」—— 用户会以为主题被重置了。所以必须搬一次。</para>
/// <para><b>幂等性有两层</b>：外层是版本标记（<see cref="ConfigMigrationRunner"/> 记在
/// <c>config-migrations.json</c>，标记丢了才重跑）；内层是结构 —— 搬完 <c>settings.json</c> 里就没有这些键了，
/// 下次运行没有可搬项，自然不写盘。中途崩溃（写完 global、没删 settings）也只会让下一轮
/// 走「两边都有 → 保留 global、删掉 settings」的分支，仍然收敛到同一状态。</para>
/// <para><b>为什么用 JsonNode 而不是强类型 SettingsJson</b>：强类型往返会丢掉本类不认识的字段
/// （例如 <c>autoFetchModels</c>、<c>update</c>），迁移只应该动被搬的那几个键。</para>
/// </summary>
public sealed class SettingsSourceMigration : IConfigMigration {
    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Name => "settings-source";

    /// <summary>
    /// 写盘选项 —— 继承 <see cref="ConfigIndentedJsonContext"/> 的缩进与源码生成 resolver，
    /// 只额外换成允许真实中文的 Encoder（默认编码器会把用户手写的中文转成转义序列）。
    /// </summary>
    private static JsonSerializerOptions WriteOptions => ConfigIndentedJsonContext.Default.RelaxedOptions();

    /// <inheritdoc/>
    public async Task<ConfigMigrationOutcome> ApplyAsync(
        IFileSystem fs, ILogger? logger, CancellationToken cancellationToken) {
        var settingsPath = AppDataConstants.Paths.SettingsFilePath;
        var globalPath = AppDataConstants.Paths.GlobalConfigFilePath;

        if (!fs.FileExists(settingsPath))
            return ConfigMigrationOutcome.None;

        var moved = new List<string>();
        var conflicted = new List<string>();

        // 1. 先读 settings.json 找出待搬的键（global 源 + current 分支里有值）
        var pending = await ReadPendingMovesAsync(settingsPath, fs, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
            return ConfigMigrationOutcome.None;

        // 2. 先写 global.json —— 万一后续删 settings 失败，值也不会丢；
        //    下一轮会走「两边都有」分支收敛。
        var globalNode = await ReadJsonObjectAsync(globalPath, fs, cancellationToken).ConfigureAwait(false)
            ?? new JsonObject();

        var globalChanged = false;
        var toRemove = new List<string>(pending.Count);
        foreach (var (key, value) in pending) {
            if (globalNode[key] is not null) {
                // 两边都有值 —— global 是今后唯一权威源，保留它。
                // 这里只记日志不静默：用户若发现主题不对，能查到原因。
                conflicted.Add(key);
            } else {
                globalNode[key] = JsonNode.Parse(value);
                globalChanged = true;
                moved.Add(key);
            }
            toRemove.Add(key);
        }

        if (globalChanged) {
            await WriteJsonObjectAsync(globalPath, globalNode, fs, cancellationToken).ConfigureAwait(false);
        }

        // 3. 再清 settings.json 里的同一批键
        await RemoveKeysAsync(settingsPath, toRemove, fs, cancellationToken).ConfigureAwait(false);

        if (moved.Count > 0) {
            logger?.LogInformation(
                "配置迁移：{Count} 个键从 settings.json 移到 global.json（{Keys}）",
                moved.Count,
                string.Join(", ", moved));
        }

        if (conflicted.Count > 0) {
            logger?.LogWarning(
                "配置迁移：{Count} 个键在 global.json 已有值，保留 global 并删除 settings.json 的旧值（{Keys}）",
                conflicted.Count,
                string.Join(", ", conflicted));
        }

        return new ConfigMigrationOutcome(moved, conflicted);
    }

    /// <summary>读出 settings.json 里「该在 global 源、却还在新位置有值」的键及其原始 JSON 文本。</summary>
    private static async Task<List<(string Key, string Value)>> ReadPendingMovesAsync(
        string settingsPath, IFileSystem fs, CancellationToken cancellationToken) {
        var result = new List<(string, string)>();
        var root = await ReadJsonObjectAsync(settingsPath, fs, cancellationToken).ConfigureAwait(false);
        if (root?["current"] is not JsonObject current)
            return result;

        foreach (var (key, setting) in JoinCode.Abstractions.Configuration.Settings.ConfigKeyRegistry.All) {
            if (setting.Source != SettingSource.GlobalConfig)
                continue;

            var node = current[key];
            if (node is null)
                continue;

            // 布尔/数字要按原类型搬，字符串要带引号 —— 直接搬原始 JSON 文本最稳。
            result.Add((key, node.ToJsonString()));
        }

        return result;
    }

    /// <summary>从 settings.json 的 current 分支删除指定键（保留文件其它内容与原编码）。</summary>
    private static async Task RemoveKeysAsync(
        string settingsPath, List<string> keys, IFileSystem fs, CancellationToken cancellationToken) {
        await fs.EditFileAsync<bool>(settingsPath, async (bytes, ct) => {
            var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
            JsonObject root;
            try {
                root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
            } catch (JsonException) {
                return new EditFileTransformResult<bool>(null, false);
            }

            if (root["current"] is not JsonObject current)
                return new EditFileTransformResult<bool>(null, false);

            var removed = false;
            foreach (var key in keys) {
                if (current.Remove(key)) removed = true;
            }

            if (!removed)
                return new EditFileTransformResult<bool>(null, false);

            var output = root.ToJsonString(WriteOptions);
            return new EditFileTransformResult<bool>(FileEncodingDetector.EncodeString(output, encoding), true);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读 JSON 对象；文件不存在或内容损坏返回 null（调用方按「空对象」处理）。</summary>
    private static async Task<JsonObject?> ReadJsonObjectAsync(
        string path, IFileSystem fs, CancellationToken cancellationToken) {
        if (!fs.FileExists(path))
            return null;

        var bytes = await fs.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var (content, _) = FileEncodingDetector.DecodeBytes(bytes);
        try {
            return JsonNode.Parse(content) as JsonObject;
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>写 JSON 对象（保留原文件编码；文件不存在时写 UTF-8）。</summary>
    private static async Task WriteJsonObjectAsync(
        string path, JsonObject root, IFileSystem fs, CancellationToken cancellationToken) {
        DirectoryHelper.EnsureDirectoryExists(fs, Path.GetDirectoryName(path));

        if (fs.FileExists(path)) {
            await fs.EditFileAsync<bool>(path, async (bytes, ct) => {
                var (_, encoding) = FileEncodingDetector.DecodeBytes(bytes);
                return new EditFileTransformResult<bool>(FileEncodingDetector.EncodeString(root.ToJsonString(WriteOptions), encoding), true);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await fs.WriteAllTextAsync(path, root.ToJsonString(WriteOptions), cancellationToken).ConfigureAwait(false);
    }
}
