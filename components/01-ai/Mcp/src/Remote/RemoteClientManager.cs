
using JoinCode.Abstractions.Attributes;
using Infrastructure.Pipeline;

namespace McpToolRegistry;

[Register]
public sealed partial class RemoteClientManager : IRemoteClientManager
{
    private const int MaxReconnectAttempts = 5;
    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 30000;

    private readonly Dictionary<string, McpClientEntry> _remoteClients = new();
    private readonly Dictionary<string, List<ToolSpec>> _lastKnownToolSpecs = new();
    private readonly Dictionary<string, CancellationTokenSource> _reconnectCtsMap = new();
    private readonly SemaphoreSlim _remoteClientsLock = new(1, 1);
    private readonly IToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<RemoteClientManager> _logger;
    [Inject] private readonly IClockService _clock;
    private readonly McpReconnectAcceptLevel _acceptLevel;
    private readonly MiddlewarePipeline<RemoteSyncContext>? _syncPipeline;

    public event EventHandler<ToolsListChangedEventArgs>? ToolsListChanged;
    public event EventHandler<ResourcesListChangedEventArgs>? ResourcesListChanged;
    public event EventHandler<PromptsListChangedEventArgs>? PromptsListChanged;

    public RemoteClientManager(
        IToolRegistry toolRegistry,
        ILogger<RemoteClientManager> logger,
        McpReconnectAcceptLevel acceptLevel = McpReconnectAcceptLevel.IdentityOnly,
        IEnumerable<IRemoteSyncMiddleware>? syncMiddlewares = null,
        IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        _toolRegistry = toolRegistry;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _acceptLevel = acceptLevel;

        if (syncMiddlewares is not null)
        {
            _syncPipeline = new MiddlewarePipeline<RemoteSyncContext>(syncMiddlewares);
        }
    }

    private void OnClientNotificationReceived(string clientId, McpNotificationReceivedEventArgs args)
    {
        var method = McpMethodExtensions.FromValue(args.Method);
        switch (method)
        {
            case McpMethod.NotificationToolsListChanged:
                _logger.LogInformation("远程客户端 {ClientId} 发送工具列表变更通知，触发自动同步", clientId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await SyncToolsAsync(clientId).ConfigureAwait(false);

                        ToolsListChanged?.Invoke(this, new ToolsListChangedEventArgs
                        {
                            ClientId = clientId,
                            SyncResult = result
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理远程客户端 {ClientId} 工具变更通知时同步失败", clientId);
                    }
                });
                break;

            case McpMethod.NotificationResourcesListChanged:
                _logger.LogInformation("远程客户端 {ClientId} 发送资源列表变更通知，触发自动同步", clientId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await SyncResourcesAsync(clientId).ConfigureAwait(false);

                        ResourcesListChanged?.Invoke(this, new ResourcesListChangedEventArgs
                        {
                            ClientId = clientId,
                            SyncResult = result
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理远程客户端 {ClientId} 资源变更通知时同步失败", clientId);
                    }
                });
                break;

            case McpMethod.NotificationPromptsListChanged:
                _logger.LogInformation("远程客户端 {ClientId} 发送提示模板列表变更通知，触发自动同步", clientId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await SyncPromptsAsync(clientId).ConfigureAwait(false);

                        PromptsListChanged?.Invoke(this, new PromptsListChangedEventArgs
                        {
                            ClientId = clientId,
                            SyncResult = result
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理远程客户端 {ClientId} 提示模板变更通知时同步失败", clientId);
                    }
                });
                break;
        }
    }

    private void OnClientConnectionLost(string clientId, McpConnectionLostEventArgs args)
    {
        _logger.LogWarning("远程客户端 {ClientId} 连接丢失 (Transport={TransportType})", clientId, args.TransportType);

        if (args.TransportType == "stdio")
        {
            _logger.LogInformation("Stdio 客户端 {ClientId} 不自动重连，标记为断开", clientId);
            return;
        }

        _ = Task.Run(() => ReconnectWithBackoffAsync(clientId, args.TransportType));
    }

    private async Task ReconnectWithBackoffAsync(string clientId, string transportType)
    {
        await _remoteClientsLock.WaitAsync();
        try
        {
            if (_reconnectCtsMap.TryGetValue(clientId, out var existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _reconnectCtsMap[clientId] = cts;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        var reconnectCts = _reconnectCtsMap.GetValueOrDefault(clientId);
        if (reconnectCts == null) return;

        try
        {
            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                if (reconnectCts.IsCancellationRequested) return;

                _logger.LogInformation(
                    "远程客户端 {ClientId} 重连尝试 {Attempt}/{Max} (Transport={TransportType})",
                    clientId, attempt, MaxReconnectAttempts, transportType);

                try
                {
                    await ReconnectClientAsync(clientId, reconnectCts.Token).ConfigureAwait(false);

                    _logger.LogInformation("远程客户端 {ClientId} 重连成功", clientId);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "远程客户端 {ClientId} 重连尝试 {Attempt} 失败",
                        clientId, attempt);

                    if (attempt == MaxReconnectAttempts)
                    {
                        _logger.LogError("远程客户端 {ClientId} 在 {Max} 次重连后仍然失败，放弃重连",
                            clientId, MaxReconnectAttempts);
                        return;
                    }

                    var backoffMs = Math.Min(InitialBackoffMs * (1 << (attempt - 1)), MaxBackoffMs);
                    await Task.Delay(backoffMs, reconnectCts.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await _remoteClientsLock.WaitAsync();
            try
            {
                if (_reconnectCtsMap.TryGetValue(clientId, out var cts) && cts == reconnectCts)
                {
                    _reconnectCtsMap.Remove(clientId);
                }
                reconnectCts.Dispose();
            }
            finally
            {
                _remoteClientsLock.Release();
            }
        }
    }

    private async Task ReconnectClientAsync(string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        if (client == null)
        {
            throw new InvalidOperationException($"客户端 '{clientId}' 未找到");
        }

        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"RemoteClientManager: Disconnect failed during reconnect for client: {ex.Message}");
        }

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        var syncResult = await SyncToolsAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!syncResult.Success)
        {
            _logger.LogWarning("重连后同步工具失败: {Error}", syncResult.ErrorMessage);
        }
    }

    /// <summary>
    /// 注册远程 MCP 客户端（异步）
    /// </summary>
    public async Task RegisterClientAsync(string clientId, IMcpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(client);

        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            if (_remoteClients.ContainsKey(clientId))
            {
                throw new InvalidOperationException($"远程客户端 '{clientId}' 已注册");
            }

            _remoteClients[clientId] = new McpClientEntry
            {
                ClientId = clientId,
                Client = client,
                RegisteredAt = _clock.GetUtcNow()
            };

            client.NotificationReceived += (sender, args) => OnClientNotificationReceived(clientId, args);
            client.ConnectionLost += (sender, args) => OnClientConnectionLost(clientId, args);

            _logger.LogInformation("已注册远程 MCP 客户端: {ClientId}", clientId);
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    /// <summary>
    /// 注销远程 MCP 客户端
    /// </summary>
    public async Task<bool> UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            if (_remoteClients.TryGetValue(clientId, out var entry))
            {
                await entry.Client.DisposeAsync();
                _remoteClients.Remove(clientId);
                _lastKnownToolSpecs.Remove(clientId);

                if (_reconnectCtsMap.TryGetValue(clientId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _reconnectCtsMap.Remove(clientId);
                }

                _logger.LogInformation("已移除远程 MCP 客户端: {ClientId}", clientId);
                return true;
            }

            return false;
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    /// <summary>
    /// 获取远程客户端（异步）
    /// </summary>
    public async Task<IMcpClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            return _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    /// <summary>
    /// 获取所有远程客户端（异步）
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IMcpClient>> GetAllClientsAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            return _remoteClients
                .ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.Client);
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    public async Task<RemoteToolsSyncResult> SyncToolsAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        if (_syncPipeline is not null)
        {
            return await SyncToolsViaPipelineAsync(clientId, cancellationToken).ConfigureAwait(false);
        }

        return await SyncToolsDirectAsync(clientId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteToolsSyncResult> SyncToolsViaPipelineAsync(string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        List<ToolSpec>? previousSpecs;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
            _lastKnownToolSpecs.TryGetValue(clientId, out previousSpecs);
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        var ctx = new RemoteSyncContext
        {
            ClientId = clientId,
            Operation = RemoteSyncOperation.Tools,
            AcceptLevel = _acceptLevel,
            CancellationToken = cancellationToken,
            Client = client,
            PreviousToolSpecs = previousSpecs,
        };

        var pipeline = _syncPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.SyncedNames.Count > 0)
        {
            await _remoteClientsLock.WaitAsync(cancellationToken);
            try
            {
                var newSpecs = ctx.ToolsResult?.Data!
                    .Select(t => new ToolSpec(
                        McpNameNormalizer.BuildMcpToolName(clientId, t.Name),
                        t.Description,
                        t.InputSchema?.ToString()))
                    .ToList();
                if (newSpecs is not null)
                {
                    _lastKnownToolSpecs[clientId] = newSpecs;
                }
            }
            finally
            {
                _remoteClientsLock.Release();
            }
        }

        return new RemoteToolsSyncResult(
            ctx.Success,
            ctx.SyncedNames,
            ctx.ErrorMessage,
            ctx.DriftReport,
            ctx.ReconnectResult);
    }

    private async Task<RemoteToolsSyncResult> SyncToolsDirectAsync(
        string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        List<ToolSpec>? previousSpecs;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
            _lastKnownToolSpecs.TryGetValue(clientId, out previousSpecs);
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        if (client == null)
        {
            return new RemoteToolsSyncResult(false, Array.Empty<string>(), $"客户端 '{clientId}' 未找到");
        }

        try
        {
            var toolsResult = await client.ListToolsAsync(cancellationToken);

            if (!toolsResult.Success)
            {
                return new RemoteToolsSyncResult(false, Array.Empty<string>(), toolsResult.ErrorMessage);
            }

            var newSpecs = toolsResult.Data!
                .Select(t => new ToolSpec(
                    McpNameNormalizer.BuildMcpToolName(clientId, t.Name),
                    t.Description,
                    t.InputSchema?.ToString()))
                .ToList();

            ToolDriftReport? driftReport = null;
            McpReconnectResult? reconnectResult = null;
            if (previousSpecs is { Count: > 0 })
            {
                driftReport = ToolListDriftClassifier.Classify(previousSpecs, newSpecs);
                _logger.LogInformation(
                    "远程客户端 {ClientId} 工具漂移检测: {DriftKind} - {Summary}",
                    clientId, driftReport.Kind, driftReport.Summary);

                if (!driftReport.IsCacheSafe)
                {
                    _logger.LogWarning(
                        "远程客户端 {ClientId} 检测到缓存不安全漂移: {DriftKind}，前缀缓存可能失效",
                        clientId, driftReport.Kind);
                }

                reconnectResult = McpReconnectPolicy.Decide(driftReport, _acceptLevel);
                if (!reconnectResult.Accepted)
                {
                    _logger.LogWarning(
                        "远程客户端 {ClientId} 重连策略拒绝同步: {Reason}",
                        clientId, reconnectResult.Reason);

                    return new RemoteToolsSyncResult(
                        false, Array.Empty<string>(), reconnectResult.Reason,
                        driftReport, reconnectResult);
                }
            }

            var toolItems = toolsResult.Data!
                .Select(tool =>
                {
                    var remoteToolHandler = new RemoteMcpToolHandler(clientId, client, tool);
                    var fullToolName = McpNameNormalizer.BuildMcpToolName(clientId, tool.Name);
                    return (FullToolName: fullToolName, Handler: remoteToolHandler);
                })
                .ToList();

            await Task.WhenAll(toolItems.Select(item => _toolRegistry.RegisterToolAsync(item.Handler, cancellationToken))).ConfigureAwait(false);

            await _remoteClientsLock.WaitAsync(cancellationToken);
            try
            {
                _lastKnownToolSpecs[clientId] = newSpecs;
            }
            finally
            {
                _remoteClientsLock.Release();
            }

            _logger.LogInformation(
                "从远程客户端 {ClientId} 同步了 {Count} 个工具",
                clientId,
                toolItems.Count);

            return new RemoteToolsSyncResult(true, toolItems.Select(t => t.FullToolName).ToList(), DriftReport: driftReport, ReconnectResult: reconnectResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从远程客户端 {ClientId} 同步工具失败", clientId);
            return new RemoteToolsSyncResult(false, Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>
    /// 从远程客户端同步资源（异步）
    /// </summary>
    public async Task<OperationResult<IReadOnlyList<string>>> SyncResourcesAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        if (_syncPipeline is not null)
        {
            return await SyncResourcesViaPipelineAsync(clientId, cancellationToken).ConfigureAwait(false);
        }

        return await SyncResourcesDirectAsync(clientId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<IReadOnlyList<string>>> SyncResourcesViaPipelineAsync(string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        var ctx = new RemoteSyncContext
        {
            ClientId = clientId,
            Operation = RemoteSyncOperation.Resources,
            CancellationToken = cancellationToken,
            Client = client,
        };

        var pipeline = _syncPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        return ctx.Success
            ? OperationResult<IReadOnlyList<string>>.Ok(ctx.SyncedNames)
            : OperationResult<IReadOnlyList<string>>.Fail(ctx.ErrorMessage ?? "Unknown error");
    }

    private async Task<OperationResult<IReadOnlyList<string>>> SyncResourcesDirectAsync(
        string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        if (client == null)
        {
            return OperationResult<IReadOnlyList<string>>.Fail($"客户端 '{clientId}' 未找到");
        }

        try
        {
            var resourcesResult = await client.ListResourcesAsync(cancellationToken);

            if (!resourcesResult.Success)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(resourcesResult.ErrorMessage ?? "Unknown error");
            }

            var resourceUris = resourcesResult.Data!
                .Select(r => r.Uri)
                .ToList();

            _logger.LogInformation(
                "从远程客户端 {ClientId} 同步了 {Count} 个资源",
                clientId,
                resourceUris.Count);

            return OperationResult<IReadOnlyList<string>>.Ok(resourceUris);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从远程客户端 {ClientId} 同步资源失败", clientId);
            return OperationResult<IReadOnlyList<string>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 从远程客户端同步提示模板（异步）
    /// </summary>
    public async Task<OperationResult<IReadOnlyList<string>>> SyncPromptsAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        if (_syncPipeline is not null)
        {
            return await SyncPromptsViaPipelineAsync(clientId, cancellationToken).ConfigureAwait(false);
        }

        return await SyncPromptsDirectAsync(clientId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<IReadOnlyList<string>>> SyncPromptsViaPipelineAsync(string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        var ctx = new RemoteSyncContext
        {
            ClientId = clientId,
            Operation = RemoteSyncOperation.Prompts,
            CancellationToken = cancellationToken,
            Client = client,
        };

        var pipeline = _syncPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        return ctx.Success
            ? OperationResult<IReadOnlyList<string>>.Ok(ctx.SyncedNames)
            : OperationResult<IReadOnlyList<string>>.Fail(ctx.ErrorMessage ?? "Unknown error");
    }

    private async Task<OperationResult<IReadOnlyList<string>>> SyncPromptsDirectAsync(
        string clientId, CancellationToken cancellationToken)
    {
        IMcpClient? client;
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            client = _remoteClients.GetValueOrDefault(clientId)?.Client;
        }
        finally
        {
            _remoteClientsLock.Release();
        }

        if (client == null)
        {
            return OperationResult<IReadOnlyList<string>>.Fail($"客户端 '{clientId}' 未找到");
        }

        try
        {
            var promptsResult = await client.ListPromptsAsync(cancellationToken);

            if (!promptsResult.Success)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(promptsResult.ErrorMessage ?? "Unknown error");
            }

            var promptNames = promptsResult.Data!
                .Select(p => p.Name)
                .ToList();

            _logger.LogInformation(
                "从远程客户端 {ClientId} 同步了 {Count} 个提示模板",
                clientId,
                promptNames.Count);

            return OperationResult<IReadOnlyList<string>>.Ok(promptNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从远程客户端 {ClientId} 同步提示模板失败", clientId);
            return OperationResult<IReadOnlyList<string>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 获取远程客户端数量（异步）
    /// </summary>
    public async Task<int> GetClientCountAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            return _remoteClients.Count;
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    /// <summary>
    /// 清除所有远程客户端
    /// </summary>
    public async Task ClearAllClientsAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClientsLock.WaitAsync(cancellationToken);
        try
        {
            await Task.WhenAll(_remoteClients.Values
                .Select(entry => entry.Client.DisposeAsync().AsTask()));

            _remoteClients.Clear();
            _lastKnownToolSpecs.Clear();
            _logger.LogInformation("所有远程 MCP 客户端已清除");
        }
        finally
        {
            _remoteClientsLock.Release();
        }
    }

    /// <summary>
    /// 清除缓存（工具规格缓存等）
    /// </summary>
    public void ClearCache()
    {
        _lastKnownToolSpecs.Clear();
        _logger.LogDebug("RemoteClientManager cache cleared");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _remoteClientsLock.WaitAsync();
        try
        {
            foreach (var cts in _reconnectCtsMap.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _reconnectCtsMap.Clear();

            await Task.WhenAll(_remoteClients.Values
                .Select(entry => entry.Client.DisposeAsync().AsTask()));
            _remoteClients.Clear();
            _lastKnownToolSpecs.Clear();
        }
        finally
        {
            _remoteClientsLock.Release();
            _remoteClientsLock.Dispose();
        }
    }
}
