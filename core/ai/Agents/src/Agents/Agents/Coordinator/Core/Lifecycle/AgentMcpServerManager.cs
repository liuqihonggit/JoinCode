namespace Core.Agents.Coordinator;

/// <summary>
/// Agent MCP 服务器管理器 - 负责 Agent 级别的 MCP 服务器初始化和清理
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentMcpServerManager))]
public sealed partial class AgentMcpServerManager : JoinCode.Abstractions.Interfaces.IAgentMcpServerManager
{
    private readonly IRemoteClientManager _remoteClientManager;
    [Inject] private readonly ILogger<AgentMcpServerManager>? _logger;
    private readonly ConcurrentDictionary<string, List<string>> _agentClients = new(StringComparer.Ordinal);
    private readonly IMcpAuthConfigProvider? _authConfigProvider;
    private readonly IMcpClientFactory? _mcpClientFactory;

    public AgentMcpServerManager(
        IRemoteClientManager remoteClientManager,
        ILogger<AgentMcpServerManager>? logger = null,
        IMcpAuthConfigProvider? authConfigProvider = null,
        IMcpClientFactory? mcpClientFactory = null)
    {
        _remoteClientManager = remoteClientManager ?? throw new ArgumentNullException(nameof(remoteClientManager));
        _logger = logger;
        _authConfigProvider = authConfigProvider;
        _mcpClientFactory = mcpClientFactory;
    }

    public async Task<JoinCode.Abstractions.Interfaces.AgentMcpServerResult> InitializeAgentMcpServersAsync(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition agentDefinition,
        IReadOnlyList<string>? parentClientIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentDefinition);

        // 获取当前所有父级 client IDs（如果未显式传入）
        var effectiveParentClientIds = parentClientIds;
        if (effectiveParentClientIds is null)
        {
            try
            {
                var allClients = await _remoteClientManager.GetAllClientsAsync(cancellationToken).ConfigureAwait(false);
                effectiveParentClientIds = allClients.Keys.ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[AgentMcpServerManager] 获取父级客户端列表失败");
                effectiveParentClientIds = [];
            }
        }

        var result = new JoinCode.Abstractions.Interfaces.AgentMcpServerResult
        {
            AgentId = agentDefinition.AgentType
        };

        // 合并 parentClientIds（继承父级 MCP）
        if (effectiveParentClientIds is { Count: > 0 })
        {
            var mergedClientIds = new List<string>(effectiveParentClientIds);
            foreach (var parentId in effectiveParentClientIds)
            {
                _agentClients.TryGetValue(parentId, out var parentTools);
                if (parentTools is not null)
                {
                    mergedClientIds.AddRange(parentTools);
                }
            }
        }

        if (agentDefinition.McpServers is null or { Count: 0 })
        {
            // 没有 agent-specific MCP，仍然记录 parent 继承
            var clientIds = effectiveParentClientIds is not null && effectiveParentClientIds.Count > 0
                ? new List<string>(effectiveParentClientIds)
                : new List<string>();
            if (clientIds.Count > 0)
                _agentClients[agentDefinition.AgentType] = clientIds;
            return result;
        }

        var connectedServers = new List<JoinCode.Abstractions.Interfaces.McpConnectedServer>();
        var allToolNames = new List<string>();
        var newlyCreatedClientIds = new List<string>();

        foreach (var spec in agentDefinition.McpServers)
        {
            try
            {
                var (clientId, isNewlyCreated) = await ConnectMcpServerAsync(spec, cancellationToken).ConfigureAwait(false);
                if (clientId is null) continue;

                newlyCreatedClientIds.Add(clientId);
                connectedServers.Add(new JoinCode.Abstractions.Interfaces.McpConnectedServer
                {
                    ServerName = spec.ServerNameRef ?? "unknown",
                    ClientId = clientId,
                    IsNewlyCreated = isNewlyCreated
                });

                var syncResult = await _remoteClientManager.SyncToolsAsync(clientId, cancellationToken).ConfigureAwait(false);
                if (syncResult.Success)
                {
                    allToolNames.AddRange(syncResult.ToolNames);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Agent '{AgentType}' 的 MCP 服务器 '{ServerName}' 连接失败",
                    agentDefinition.AgentType, spec.ServerNameRef);
            }
        }

        // 合并 parent + agent-specific clients
        var allClientIds = new List<string>();
        if (effectiveParentClientIds is not null && effectiveParentClientIds.Count > 0)
        {
            allClientIds.AddRange(effectiveParentClientIds);
        }
        allClientIds.AddRange(newlyCreatedClientIds);

        if (allClientIds.Count > 0)
            _agentClients[agentDefinition.AgentType] = allClientIds;

        result.ConnectedServers = connectedServers;
        result.ToolNames = allToolNames;
        return result;
    }

    public async Task CleanupAgentMcpServersAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (!_agentClients.TryRemove(agentId, out var clientIds))
            return;

        foreach (var clientId in clientIds)
        {
            try
            {
                await _remoteClientManager.UnregisterClientAsync(clientId, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("已清理 Agent '{AgentId}' 的 MCP 客户端: {ClientId}", agentId, clientId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "清理 Agent '{AgentId}' 的 MCP 客户端 '{ClientId}' 失败", agentId, clientId);
            }
        }
    }

    private async Task<(string? ClientId, bool IsNewlyCreated)> ConnectMcpServerAsync(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec spec,
        CancellationToken cancellationToken)
    {
        if (spec.InlineConfig is not null)
            return await ConnectInlineServerAsync(spec.ServerNameRef ?? "inline", spec.InlineConfig, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(spec.ServerNameRef))
            return await ConnectReferencedServerAsync(spec.ServerNameRef, cancellationToken).ConfigureAwait(false);

        return (null, false);
    }

    private async Task<(string ClientId, bool IsNewlyCreated)> ConnectInlineServerAsync(
        string name, JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerInlineConfig config,
        CancellationToken cancellationToken)
    {
        var transportType = McpClientTransportTypeExtensions.FromValue(config.TransportType)
            ?? (!string.IsNullOrEmpty(config.Command) ? McpClientTransportType.Stdio : McpClientTransportType.Http);

        var endpoint = transportType == McpClientTransportType.Stdio
            ? config.Command ?? string.Empty
            : config.Url ?? string.Empty;

        McpAuthConfig? authConfig = null;
        if (!string.IsNullOrWhiteSpace(config.AuthName) && _authConfigProvider != null)
        {
            authConfig = _authConfigProvider.GetAuthConfig(config.AuthName);
            if (authConfig == null)
            {
                _logger?.LogWarning("Agent 内联 MCP 服务器 '{Name}' 引用的认证配置 '{AuthName}' 不存在",
                    name, config.AuthName);
            }
        }

        var connectionConfig = new McpServerConnectionConfig
        {
            Name = name,
            Endpoint = endpoint,
            TransportType = transportType,
            Environment = config.Env,
            Auth = authConfig
        };

        var clientId = $"agent-mcp-{name}-{Guid.NewGuid():N}";

        if (_mcpClientFactory is null)
            throw new InvalidOperationException("IMcpClientFactory 未注册，无法创建 MCP 客户端");

        IMcpClient client = _mcpClientFactory.CreateClient(connectionConfig, _logger);

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _remoteClientManager.RegisterClientAsync(clientId, client, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("已连接 Agent 内联 MCP 服务器: {Name} (ClientId={ClientId}, Transport={Transport}, Auth={HasAuth})",
            name, clientId, transportType, authConfig != null);

        return (clientId, true);
    }

    private Task<(string ClientId, bool IsNewlyCreated)> ConnectReferencedServerAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var clientId = $"agent-ref-{serverName}";
        _logger?.LogInformation("Agent 引用全局 MCP 服务器: {ServerName} (ClientId={ClientId})",
            serverName, clientId);

        return Task.FromResult((clientId, false));
    }
}
