namespace McpToolDispatch;

/// <summary>
/// 工具干预管理器 — 读取干预配置，支持运行时添加/移除干预规则
/// Blacklist→工具不注册; Downgrade→Score扣分; Redirect→注入替代建议
/// </summary>
[Register(typeof(ToolInterventionManager), ServiceLifetime.Singleton)]
public sealed class ToolInterventionManager : ServiceEntity {
    private readonly ILogger<ToolInterventionManager>? _logger;
    private readonly IFileSystem _fs;
    private ImmutableHamT<string, InterventionRule> _rules = ImmutableHamT<string, InterventionRule>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private readonly string _configPath;
    private readonly Task _loadTask;

    /// <summary>
    /// 初始化工具干预管理器，从磁盘加载已保存的干预规则
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ToolInterventionManager(IFileSystem fs, ILogger<ToolInterventionManager>? logger = null) {
        _fs = fs;
        _logger = logger;
        _configPath = Path.Combine(
            JoinCode.Abstractions.Configuration.AppData.AppDataConstants.JccDirectory,
            "tool-interventions.json");
        _loadTask = LoadFromDiskAsync();
    }

    /// <summary>
    /// 异步添加干预规则。Downgrade 类型自动扣 50 分；Redirect 类型自动推断默认重定向目标。
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="type">干预类型（Blacklist/Downgrade/Redirect）</param>
    /// <param name="reason">干预原因</param>
    /// <param name="duration">干预持续时间（可选，null 表示永久）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task AddRuleAsync(string toolName, InterventionType type, string reason, TimeSpan? duration = null, CancellationToken ct = default) {
        var rule = new InterventionRule {
            Type = type,
            Reason = reason,
            Expiry = duration.HasValue ? DateTime.UtcNow + duration.Value : null,
            ScorePenalty = type == InterventionType.Downgrade ? -50 : null,
            RedirectTo = type == InterventionType.Redirect ? GetDefaultRedirect(toolName) : null
        };
        while (true) {
            var current = _rules;
            if (Interlocked.CompareExchange(ref _rules, current.SetItem(toolName, rule), current) == current) break;
        }

        await SaveToDiskAsync().ConfigureAwait(false);
        _logger?.LogInformation("已添加工具干预: {ToolName} → {Type} ({Reason})", toolName, type, reason);

    }

    /// <summary>
    /// 异步移除指定工具的干预规则
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RemoveRuleAsync(string toolName, CancellationToken ct = default) {
        while (true) {
            var current = _rules;
            if (Interlocked.CompareExchange(ref _rules, current.Remove(toolName), current) == current) break;
        }

        await SaveToDiskAsync().ConfigureAwait(false);

    }

    /// <summary>
    /// 异步获取指定工具的干预规则（已过期的规则返回 null）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>干预规则；若不存在或已过期则返回 null</returns>
    public Task<InterventionRule?> GetRuleAsync(string toolName, CancellationToken ct = default) {
        if (_rules.TryGetValue(toolName, out var rule) && !rule.IsExpired)
            return Task.FromResult<InterventionRule?>(rule);
        return Task.FromResult<InterventionRule?>(null);

    }

    /// <summary>
    /// 异步获取所有未过期的活跃干预规则
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>以工具名为键的只读干预规则字典</returns>
    public Task<IReadOnlyDictionary<string, InterventionRule>> GetActiveRulesAsync(CancellationToken ct = default) {
        return Task.FromResult<IReadOnlyDictionary<string, InterventionRule>>(
            _rules
                .Where(kvp => !kvp.Value.IsExpired)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));

    }

    /// <summary>
    /// 判断指定工具是否被列入黑名单（且规则未过期）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <returns>若被黑名单禁用返回 true；否则 false</returns>
    public bool IsBlacklisted(string toolName) {
        if (!_rules.TryGetValue(toolName, out var rule)) return false;
        return rule.Type == InterventionType.Blacklist && !rule.IsExpired;
    }

    /// <summary>
    /// 获取指定工具的评分惩罚值（仅 Downgrade 类型且未过期）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <returns>评分惩罚值；若不存在或非 Downgrade 类型则返回 null</returns>
    public int? GetScorePenalty(string toolName) {
        if (!_rules.TryGetValue(toolName, out var rule) || rule.IsExpired) return null;
        return rule.Type == InterventionType.Downgrade ? rule.ScorePenalty : null;
    }

    internal static string? GetDefaultRedirect(string toolName) {
        return toolName.ToLowerInvariant() switch {
            "cmd" => ShellToolNameEnumConstants.Powershell,
            ShellToolNameEnumConstants.Bash => ShellToolNameEnumConstants.Powershell,
            _ => null
        };
    }

    private async Task LoadFromDiskAsync() {
        try {
            if (!_fs.FileExists(_configPath)) return;
            var json = await _fs.ReadAllText(_configPath).ConfigureAwait(false);
            var data = RelaxedJsonSerializer.Deserialize(json, ToolInterventionJsonContext.Default.DictionaryStringInterventionRule);
            if (data is null) return;
            while (true) {
                var current = _rules;
                var updated = current;
                foreach (var kvp in data)
                    updated = updated.SetItem(kvp.Key, kvp.Value);
                if (Interlocked.CompareExchange(ref _rules, updated, current) == current) return;
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "加载工具干预配置失败");
        }
    }

    private async Task SaveToDiskAsync() {
        try {
            var dir = Path.GetDirectoryName(_configPath)!;
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var snapshot = _rules;
            var dict = snapshot.ToDictionary();
            var json = RelaxedJsonSerializer.Serialize(dict, ToolInterventionJsonContext.Default);
            await _fs.WriteAllText(_configPath, json).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "保存工具干预配置失败");
        }
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() => base.Dispose();

    /// <summary>异步释放资源 — 等待加载任务完成后再释放。</summary>
    public override async ValueTask DisposeAsync() {
        if (_loadTask is not null) {
            try { await _loadTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(Dictionary<string, InterventionRule>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolInterventionJsonContext : JsonSerializerContext;