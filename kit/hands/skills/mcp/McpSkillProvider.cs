
namespace Core.Skills.Mcp;

/// <summary>
/// MCP 技能提供者 — 管理多个 MCP 客户端，聚合远程技能并按命名空间隔离
/// </summary>
[Register(typeof(IMcpSkillProvider), ServiceLifetime.Singleton)]
public sealed partial class McpSkillProvider : IMcpSkillProvider {
    private readonly ConcurrentDictionary<string, IMcpClient> _clients;
    private readonly ConcurrentDictionary<string, SkillDefinition> _mcpSkills;
    private readonly ConcurrentDictionary<string, McpSkillAdapter> _adapters;
    private readonly ILogger<McpSkillProvider>? _logger;
    private readonly AsyncLock _refreshLock = new();
    private bool _isDisposed;

    /// <summary>
    /// 创建 MCP 技能提供者
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public McpSkillProvider(ILogger<McpSkillProvider>? logger = null) {
        _clients = new ConcurrentDictionary<string, IMcpClient>(StringComparer.OrdinalIgnoreCase);
        _mcpSkills = new ConcurrentDictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
        _adapters = new ConcurrentDictionary<string, McpSkillAdapter>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;

    }

    /// <summary>
    /// 注册 MCP 客户端 — 同时创建对应的适配器
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="client">MCP 客户端实例</param>
    public void RegisterClient(string serverName, IMcpClient client) {
        ArgumentException.ThrowIfNullOrEmpty(serverName);
        ArgumentNullException.ThrowIfNull(client);

        _clients[serverName] = client;
        _adapters[serverName] = new McpSkillAdapter(client, _logger as ILogger<McpSkillAdapter>);

        _logger?.LogInformation("[McpSkillProvider] 注册 MCP 客户端: {ServerName}", serverName);
    }

    /// <summary>
    /// 注销 MCP 客户端 — 同时移除该服务器提供的所有技能
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>注销成功返回 true，否则返回 false</returns>
    public bool UnregisterClient(string serverName) {
        ArgumentException.ThrowIfNullOrEmpty(serverName);

        var removedSkills = _mcpSkills
            .Where(kvp => kvp.Value.Namespace == $"mcp.{serverName}")
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var skillName in removedSkills) {
            _mcpSkills.TryRemove(skillName, out _);
        }

        _adapters.TryRemove(serverName, out _);
        var removed = _clients.TryRemove(serverName, out _);

        if (removed) {
            _logger?.LogInformation("[McpSkillProvider] 注销 MCP 客户端: {ServerName}，移除 {Count} 个技能",
                serverName, removedSkills.Count);
        }

        return removed;
    }

    /// <summary>
    /// 异步获取所有 MCP 技能 — 首次调用会触发刷新
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>MCP 技能定义列表</returns>
    public async Task<IReadOnlyList<SkillDefinition>> GetMcpSkillsAsync(CancellationToken cancellationToken = default) {
        if (_mcpSkills.Count == 0) {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _mcpSkills.Values.ToList();
    }

    /// <summary>
    /// 按名称异步获取单个 MCP 技能定义 — O(1) 字典查找
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能定义；不存在则返回 null</returns>
    public async Task<SkillDefinition?> TryGetSkillAsync(string skillName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        if (_mcpSkills.Count == 0) {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _mcpSkills.GetValueOrDefault(skillName);
    }

    /// <summary>
    /// 异步执行 MCP 远程技能 — 通过适配器转发到对应 MCP 客户端
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="parameters">调用参数</param>
    /// <param name="ctx">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能执行结果</returns>
    public async Task<SkillResult> ExecuteMcpSkillAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        if (!_mcpSkills.TryGetValue(skillName, out var skill)) {
            return SkillResult.FailureResult(skillName, $"MCP 技能不存在: {skillName}");
        }

        var adapter = FindAdapterForSkill(skill);
        if (adapter == null) {
            return SkillResult.FailureResult(skillName, $"找不到 MCP 适配器: {skillName}");
        }

        return await adapter.ExecuteToolAsync(skillName, parameters, ctx, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步刷新所有 MCP 服务器的技能列表 — 清空缓存后重新拉取
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default) {
        using var guard = await _refreshLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_refreshLock.Name}' 等待超时");

        _mcpSkills.Clear();

        foreach (var (serverName, client) in _clients) {
            try {
                if (!client.IsConnected) {
                    await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                var toolsResult = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                if (!toolsResult.Success) {
                    _logger?.LogWarning("[McpSkillProvider] 获取 MCP 服务器 {Server} 工具列表失败: {Error}",
                        serverName, toolsResult.ErrorMessage);
                    continue;
                }

                var adapter = _adapters.GetValueOrDefault(serverName);
                if (adapter == null) {
                    continue;
                }

                foreach (var tool in toolsResult.GetData()) {
                    var skill = await adapter.AdaptToolAsync(tool, cancellationToken).ConfigureAwait(false);
                    if (skill != null) {
                        var namespacedSkill = skill with { Namespace = $"mcp.{serverName}" };
                        _mcpSkills[namespacedSkill.Name] = namespacedSkill;
                    }
                }

                _logger?.LogInformation("[McpSkillProvider] 从 MCP 服务器 {Server} 加载 {Count} 个技能",
                    serverName, toolsResult.GetData().Count);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[McpSkillProvider] 刷新 MCP 服务器 {Server} 失败", serverName);
            }
        }

    }

    /// <summary>
    /// 判断指定名称的 MCP 技能是否可用
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <returns>可用返回 true，否则返回 false</returns>
    public bool IsSkillAvailable(string skillName) {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        return _mcpSkills.ContainsKey(skillName);
    }

    /// <summary>
    /// 异步释放资源 — 释放所有 MCP 客户端并清空缓存
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync() {
        if (_isDisposed) {
            return;
        }

        _isDisposed = true;

        foreach (var (_, client) in _clients) {
            try {
                await client.DisposeAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[McpSkillProvider] 释放 MCP 客户端失败");
            }
        }

        _clients.Clear();
        _mcpSkills.Clear();
        _adapters.Clear();
        _refreshLock.Dispose();
    }

    private McpSkillAdapter? FindAdapterForSkill(SkillDefinition skill) {
        if (skill.Namespace == null) {
            return _adapters.Values.FirstOrDefault();
        }

        var prefix = "mcp.";
        if (!skill.Namespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return _adapters.Values.FirstOrDefault();
        }

        var serverName = skill.Namespace[prefix.Length..];
        return _adapters.GetValueOrDefault(serverName);
    }
}