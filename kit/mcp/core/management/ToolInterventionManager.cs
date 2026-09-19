namespace McpToolDispatch;

/// <summary>
/// 工具干预管理器 — 读取干预配置，支持运行时添加/移除干预规则
/// Blacklist→工具不注册; Downgrade→Score扣分; Redirect→注入替代建议
/// </summary>
[Register(typeof(ToolInterventionManager), ServiceLifetime.Singleton)]
public sealed class ToolInterventionManager : ServiceEntity {
    private readonly ILogger<ToolInterventionManager>? _logger;
    private readonly IFileSystem _fs;
    private readonly Dictionary<string, InterventionRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLock _lock = new();
    private readonly string _configPath;

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
        LoadFromDisk();
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
        using (var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _rules[toolName] = new InterventionRule {
                Type = type,
                Reason = reason,
                Expiry = duration.HasValue ? DateTime.UtcNow + duration.Value : null,
                ScorePenalty = type == InterventionType.Downgrade ? -50 : null,
                RedirectTo = type == InterventionType.Redirect ? GetDefaultRedirect(toolName) : null
            };
        }

        SaveToDisk();
        _logger?.LogInformation("已添加工具干预: {ToolName} → {Type} ({Reason})", toolName, type, reason);

    }

    /// <summary>
    /// 异步移除指定工具的干预规则
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RemoveRuleAsync(string toolName, CancellationToken ct = default) {
        using (var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _rules.Remove(toolName);
        }

        SaveToDisk();

    }

    /// <summary>
    /// 异步获取指定工具的干预规则（已过期的规则返回 null）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>干预规则；若不存在或已过期则返回 null</returns>
    public async Task<InterventionRule?> GetRuleAsync(string toolName, CancellationToken ct = default) {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (_rules.TryGetValue(toolName, out var rule) && !rule.IsExpired)
            return rule;
        return null;

    }

    /// <summary>
    /// 异步获取所有未过期的活跃干预规则
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>以工具名为键的只读干预规则字典</returns>
    public async Task<IReadOnlyDictionary<string, InterventionRule>> GetActiveRulesAsync(CancellationToken ct = default) {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _rules
            .Where(kvp => !kvp.Value.IsExpired)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

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

    private static string? GetDefaultRedirect(string toolName) {
        return toolName.ToLowerInvariant() switch {
            "cmd" => "powershell",
            "bash" => "powershell",
            _ => null
        };
    }

    private void LoadFromDisk() {
        try {
            if (!_fs.FileExists(_configPath)) return;
            var json = _fs.ReadAllText(_configPath);
            var data = RelaxedJsonSerializer.Deserialize(json, ToolInterventionJsonContext.Default.DictionaryStringInterventionRule);
            if (data is null) return;
            foreach (var kvp in data)
                _rules[kvp.Key] = kvp.Value;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "加载工具干预配置失败");
        }
    }

    private void SaveToDisk() {
        try {
            var dir = Path.GetDirectoryName(_configPath)!;
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var json = RelaxedJsonSerializer.Serialize(_rules, ToolInterventionJsonContext.Default);
            _fs.WriteAllText(_configPath, json);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "保存工具干预配置失败");
        }
    }

    /// <summary>释放资源 — 释放异步锁。</summary>
    public override void Dispose() {
        _lock.Dispose();
        base.Dispose();
    }
}

[JsonSerializable(typeof(Dictionary<string, InterventionRule>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolInterventionJsonContext : JsonSerializerContext;