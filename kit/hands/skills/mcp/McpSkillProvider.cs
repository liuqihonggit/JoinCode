
namespace Core.Skills.Mcp;

/// <summary>
/// MCP 技能提供者 — 管理多个 MCP 客户端，聚合远程技能并按命名空间隔离
/// </summary>
[Register(typeof(IMcpSkillProvider), ServiceLifetime.Singleton)]
public sealed partial class McpSkillProvider : IMcpSkillProvider {
    private ImmutableDictionary<string, IMcpClient> _clients = ImmutableDictionary<string, IMcpClient>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private ImmutableDictionary<string, SkillDefinition> _mcpSkills = ImmutableDictionary<string, SkillDefinition>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private ImmutableDictionary<string, McpSkillAdapter> _adapters = ImmutableDictionary<string, McpSkillAdapter>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<McpSkillProvider>? _logger;
    private readonly AsyncLock _refreshLock = new();
    private bool _isDisposed;

    /// <summary>
    /// 创建 MCP 技能提供者
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public McpSkillProvider(ILogger<McpSkillProvider>? logger = null) {
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

        ImmutableInterlocked.Update(ref _clients, d => d.SetItem(serverName, client));
        ImmutableInterlocked.Update(ref _adapters, d => d.SetItem(serverName, new McpSkillAdapter(client, _logger as ILogger<McpSkillAdapter>)));

        _logger?.LogInformation("[McpSkillProvider] 注册 MCP 客户端: {ServerName}", serverName);
    }

    /// <summary>
    /// 注销 MCP 客户端 — 同时移除该服务器提供的所有技能
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>注销成功返回 true，否则返回 false</returns>
    public bool UnregisterClient(string serverName) {
        ArgumentException.ThrowIfNullOrEmpty(serverName);

        var snapshot = Volatile.Read(ref _mcpSkills);
        var removedSkills = snapshot
            .Where(kvp => kvp.Value.Namespace == $"mcp.{serverName}")
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var skillName in removedSkills) {
            ImmutableInterlocked.Update(ref _mcpSkills, d => d.Remove(skillName));
        }

        ImmutableInterlocked.Update(ref _adapters, d => d.Remove(serverName));
        var removed = false;
        ImmutableInterlocked.Update(ref _clients, d => {
            if (d.ContainsKey(serverName)) {
                removed = true;
                return d.Remove(serverName);
            }
            return d;
        });

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
        if (Volatile.Read(ref _mcpSkills).Count == 0) {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return Volatile.Read(ref _mcpSkills).Values.ToList();
    }

    /// <summary>
    /// 按名称异步获取单个 MCP 技能定义 — O(1) 字典查找
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能定义；不存在则返回 null</returns>
    public async Task<SkillDefinition?> TryGetSkillAsync(string skillName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        if (Volatile.Read(ref _mcpSkills).Count == 0) {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return Volatile.Read(ref _mcpSkills).GetValueOrDefault(skillName);
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

        if (!Volatile.Read(ref _mcpSkills).TryGetValue(skillName, out var skill)) {
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

        Interlocked.Exchange(ref _mcpSkills, ImmutableDictionary<string, SkillDefinition>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));

        foreach (var (serverName, client) in Volatile.Read(ref _clients)) {
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

                var adapter = Volatile.Read(ref _adapters).GetValueOrDefault(serverName);
                if (adapter == null) {
                    continue;
                }

                foreach (var tool in toolsResult.GetData()) {
                    var skill = await adapter.AdaptToolAsync(tool, cancellationToken).ConfigureAwait(false);
                    if (skill != null) {
                        var namespacedSkill = skill with { Namespace = $"mcp.{serverName}" };
                        ImmutableInterlocked.Update(ref _mcpSkills, d => d.SetItem(namespacedSkill.Name, namespacedSkill));
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
        return Volatile.Read(ref _mcpSkills).ContainsKey(skillName);
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

        foreach (var (_, client) in Interlocked.Exchange(ref _clients, ImmutableDictionary<string, IMcpClient>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase))) {
            try {
                await client.DisposeAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[McpSkillProvider] 释放 MCP 客户端失败");
            }
        }

        Interlocked.Exchange(ref _mcpSkills, ImmutableDictionary<string, SkillDefinition>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
        Interlocked.Exchange(ref _adapters, ImmutableDictionary<string, McpSkillAdapter>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
        _refreshLock.Dispose();
    }

    private McpSkillAdapter? FindAdapterForSkill(SkillDefinition skill) {
        var adapters = Volatile.Read(ref _adapters);
        if (skill.Namespace == null) {
            return adapters.Values.FirstOrDefault();
        }

        var prefix = "mcp.";
        if (!skill.Namespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return adapters.Values.FirstOrDefault();
        }

        var serverName = skill.Namespace[prefix.Length..];
        return adapters.GetValueOrDefault(serverName);
    }
}