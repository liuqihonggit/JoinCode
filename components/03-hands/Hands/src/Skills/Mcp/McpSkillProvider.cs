
namespace Core.Skills.Mcp;

[Register]
public sealed partial class McpSkillProvider : IMcpSkillProvider
{
    private readonly ConcurrentDictionary<string, IMcpClient> _clients;
    private readonly ConcurrentDictionary<string, SkillDefinition> _mcpSkills;
    private readonly ConcurrentDictionary<string, McpSkillAdapter> _adapters;
    [Inject] private readonly ILogger<McpSkillProvider>? _logger;
    private readonly SemaphoreSlim _refreshLock;
    private bool _isDisposed;

    public McpSkillProvider(ILogger<McpSkillProvider>? logger = null)
    {
        _clients = new ConcurrentDictionary<string, IMcpClient>(StringComparer.OrdinalIgnoreCase);
        _mcpSkills = new ConcurrentDictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
        _adapters = new ConcurrentDictionary<string, McpSkillAdapter>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _refreshLock = new SemaphoreSlim(1, 1);
    }

    public void RegisterClient(string serverName, IMcpClient client)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverName);
        ArgumentNullException.ThrowIfNull(client);

        _clients[serverName] = client;
        _adapters[serverName] = new McpSkillAdapter(client, _logger as ILogger<McpSkillAdapter>);

        _logger?.LogInformation("[McpSkillProvider] 注册 MCP 客户端: {ServerName}", serverName);
    }

    public bool UnregisterClient(string serverName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverName);

        var removedSkills = _mcpSkills
            .Where(kvp => kvp.Value.Namespace == $"mcp.{serverName}")
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var skillName in removedSkills)
        {
            _mcpSkills.TryRemove(skillName, out _);
        }

        _adapters.TryRemove(serverName, out _);
        var removed = _clients.TryRemove(serverName, out _);

        if (removed)
        {
            _logger?.LogInformation("[McpSkillProvider] 注销 MCP 客户端: {ServerName}，移除 {Count} 个技能",
                serverName, removedSkills.Count);
        }

        return removed;
    }

    public async Task<IReadOnlyList<SkillDefinition>> GetMcpSkillsAsync(CancellationToken cancellationToken = default)
    {
        if (_mcpSkills.Count == 0)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _mcpSkills.Values.ToList();
    }

    public async Task<SkillResult> ExecuteMcpSkillAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        if (!_mcpSkills.TryGetValue(skillName, out var skill))
        {
            return SkillResult.FailureResult(skillName, $"MCP 技能不存在: {skillName}");
        }

        var adapter = FindAdapterForSkill(skill);
        if (adapter == null)
        {
            return SkillResult.FailureResult(skillName, $"找不到 MCP 适配器: {skillName}");
        }

        return await adapter.ExecuteToolAsync(skillName, parameters, ctx, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _mcpSkills.Clear();

            foreach (var (serverName, client) in _clients)
            {
                try
                {
                    if (!client.IsConnected)
                    {
                        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }

                    var toolsResult = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                    if (!toolsResult.Success)
                    {
                        _logger?.LogWarning("[McpSkillProvider] 获取 MCP 服务器 {Server} 工具列表失败: {Error}",
                            serverName, toolsResult.ErrorMessage);
                        continue;
                    }

                    var adapter = _adapters.GetValueOrDefault(serverName);
                    if (adapter == null)
                    {
                        continue;
                    }

                    foreach (var tool in toolsResult.GetData())
                    {
                        var skill = await adapter.AdaptToolAsync(tool, cancellationToken).ConfigureAwait(false);
                        if (skill != null)
                        {
                            var namespacedSkill = skill with { Namespace = $"mcp.{serverName}" };
                            _mcpSkills[namespacedSkill.Name] = namespacedSkill;
                        }
                    }

                    _logger?.LogInformation("[McpSkillProvider] 从 MCP 服务器 {Server} 加载 {Count} 个技能",
                        serverName, toolsResult.Data!.Count);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[McpSkillProvider] 刷新 MCP 服务器 {Server} 失败", serverName);
                }
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public bool IsSkillAvailable(string skillName)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        return _mcpSkills.ContainsKey(skillName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var (_, client) in _clients)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[McpSkillProvider] 释放 MCP 客户端失败");
            }
        }

        _clients.Clear();
        _mcpSkills.Clear();
        _adapters.Clear();
        _refreshLock.Dispose();
    }

    private McpSkillAdapter? FindAdapterForSkill(SkillDefinition skill)
    {
        if (skill.Namespace == null)
        {
            return _adapters.Values.FirstOrDefault();
        }

        var prefix = "mcp.";
        if (!skill.Namespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return _adapters.Values.FirstOrDefault();
        }

        var serverName = skill.Namespace[prefix.Length..];
        return _adapters.GetValueOrDefault(serverName);
    }
}
