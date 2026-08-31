namespace McpToolDispatch;

/// <summary>
/// 工具干预管理器 — 读取干预配置，支持运行时添加/移除干预规则
/// Blacklist→工具不注册; Downgrade→Score扣分; Redirect→注入替代建议
/// </summary>
[Register(typeof(ToolInterventionManager), ServiceLifetime.Singleton)]
public sealed class ToolInterventionManager : ServiceEntity
{
    private readonly ILogger<ToolInterventionManager>? _logger;
    private readonly IFileSystem _fs;
    private readonly Dictionary<string, InterventionRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _configPath;

    public ToolInterventionManager(IFileSystem fs, ILogger<ToolInterventionManager>? logger = null)
    {
        _fs = fs;
        _logger = logger;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JoinCode",
            "tool-interventions.json");
        LoadFromDisk();
    }

    public async Task AddRuleAsync(string toolName, InterventionType type, string reason, TimeSpan? duration = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _rules[toolName] = new InterventionRule
            {
                Type = type,
                Reason = reason,
                Expiry = duration.HasValue ? DateTime.UtcNow + duration.Value : null,
                ScorePenalty = type == InterventionType.Downgrade ? -50 : null,
                RedirectTo = type == InterventionType.Redirect ? GetDefaultRedirect(toolName) : null
            };
            SaveToDisk();
            _logger?.LogInformation("已添加工具干预: {ToolName} → {Type} ({Reason})", toolName, type, reason);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveRuleAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _rules.Remove(toolName);
            SaveToDisk();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<InterventionRule?> GetRuleAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_rules.TryGetValue(toolName, out var rule) && !rule.IsExpired)
                return rule;
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, InterventionRule>> GetActiveRulesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _rules
                .Where(kvp => !kvp.Value.IsExpired)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsBlacklisted(string toolName)
    {
        if (!_rules.TryGetValue(toolName, out var rule)) return false;
        return rule.Type == InterventionType.Blacklist && !rule.IsExpired;
    }

    public int? GetScorePenalty(string toolName)
    {
        if (!_rules.TryGetValue(toolName, out var rule) || rule.IsExpired) return null;
        return rule.Type == InterventionType.Downgrade ? rule.ScorePenalty : null;
    }

    private static string? GetDefaultRedirect(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "cmd" => "powershell",
            "bash" => "powershell",
            _ => null
        };
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!_fs.FileExists(_configPath)) return;
            var json = _fs.ReadAllText(_configPath);
            var data = RelaxedJsonSerializer.Deserialize(json, ToolInterventionJsonContext.Default.DictionaryStringInterventionRule);
            if (data is null) return;
            foreach (var kvp in data)
                _rules[kvp.Key] = kvp.Value;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载工具干预配置失败");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath)!;
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var json = RelaxedJsonSerializer.Serialize(_rules, ToolInterventionJsonContext.Default);
            _fs.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存工具干预配置失败");
        }
    }

    protected override void OnDispose() => _lock.Dispose();
}

[JsonSerializable(typeof(Dictionary<string, InterventionRule>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolInterventionJsonContext : JsonSerializerContext;
