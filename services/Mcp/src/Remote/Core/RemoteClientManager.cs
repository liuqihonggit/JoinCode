namespace McpToolRegistry;

[Register(typeof(IRemoteClientManager), ServiceLifetime.Singleton)]
public sealed partial class RemoteClientManager : IRemoteClientManager
{
    private const int MaxReconnectAttempts = 5;
    private const int InitialBackoffMs = 2000;
    private const int MaxBackoffMs = 300000;

    private readonly ConcurrentDictionary<string, McpClientEntry> _remoteClients = new();
    private readonly ConcurrentDictionary<string, List<ToolSpec>> _lastKnownToolSpecs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconnectCtsMap = new();
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<RemoteClientManager> _logger;
    private readonly IClockService _clock;
    private readonly McpReconnectAcceptLevel _acceptLevel;
    private readonly MiddlewarePipeline<RemoteSyncContext>? _syncPipeline;
    private readonly INetworkConnectivityService? _networkService;

    public event EventHandler<ToolsListChangedEventArgs>? ToolsListChanged;
    public event EventHandler<ResourcesListChangedEventArgs>? ResourcesListChanged;
    public event EventHandler<PromptsListChangedEventArgs>? PromptsListChanged;

    public RemoteClientManager(
        IToolRegistry toolRegistry,
        ILogger<RemoteClientManager> logger,
        ILoggerFactory? loggerFactory = null,
        McpReconnectAcceptLevel acceptLevel = McpReconnectAcceptLevel.IdentityOnly,
        IEnumerable<IRemoteSyncMiddleware>? syncMiddlewares = null,
        IClockService? clock = null,
        INetworkConnectivityService? networkService = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        _toolRegistry = toolRegistry;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _acceptLevel = acceptLevel;
        _networkService = networkService;

        if (syncMiddlewares is not null && loggerFactory is not null)
        {
            _syncPipeline = new PipelineBuilder<RemoteSyncContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(syncMiddlewares)
                .Build();
        }
        else if (syncMiddlewares is not null)
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

    /// <summary>
    /// 等待网络恢复 — 网络不可用时阻塞等待(带 30s 超时),恢复后继续重连
    /// </summary>
    private async Task WaitForNetworkAsync(CancellationToken ct)
    {
        if (_networkService is null) return;
        if (_networkService.IsNetworkAvailable()) return;

        _logger.LogWarning("远程客户端重连:网络不可用,等待恢复...");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) =>
        {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        _networkService.StateChanged += handler;
        try
        {
            if (!_networkService.IsNetworkAvailable())
            {
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("远程客户端重连:等待网络恢复超时(30s),继续重连");
        }
        finally
        {
            _networkService.StateChanged -= handler;
        }

        _logger.LogInformation("远程客户端重连:网络已恢复");
    }

    private async Task ReconnectWithBackoffAsync(string clientId, string transportType)
    {
        SetupReconnectCts(clientId);

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

                    await WaitForNetworkAsync(reconnectCts.Token).ConfigureAwait(false);
                    var backoff = new ExponentialBackoff(
                        TimeSpan.FromMilliseconds(InitialBackoffMs),
                        TimeSpan.FromMilliseconds(MaxBackoffMs));
                    await Task.Delay(backoff.CalculateDelay(attempt - 1), reconnectCts.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            CleanupReconnectCts(clientId, reconnectCts);
        }
    }

    /// <summary>设置重连 CTS（取消旧的，创建新的）</summary>
    private void SetupReconnectCts(string clientId)
    {
        CancellationTokenSource? oldCts = null;
        if (_reconnectCtsMap.TryGetValue(clientId, out oldCts))
            _reconnectCtsMap.TryRemove(clientId, out _);

        var cts = new CancellationTokenSource();
        _reconnectCtsMap[clientId] = cts;

        oldCts?.Cancel();
        oldCts?.Dispose();
    }

    /// <summary>清理重连 CTS</summary>
    private void CleanupReconnectCts(string clientId, CancellationTokenSource reconnectCts)
    {
        if (_reconnectCtsMap.TryGetValue(clientId, out var currentCts) && currentCts == reconnectCts)
            _reconnectCtsMap.TryRemove(clientId, out _);

        reconnectCts.Dispose();
    }

    private async Task ReconnectClientAsync(string clientId, CancellationToken cancellationToken)
    {
        var client = _remoteClients.GetValueOrDefault(clientId)?.Client;

        if (client == null)
        {
            throw new InvalidOperationException($"[MCP024] 客户端 '{clientId}' 未找到");
        }

        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RemoteClientManager: Disconnect failed during reconnect for client");
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
    public Task RegisterClientAsync(string clientId, IMcpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(client);

        var entry = new McpClientEntry
        {
            ClientId = clientId,
            Client = client,
            RegisteredAt = _clock.GetUtcNow()
        };
        if (!_remoteClients.TryAdd(clientId, entry))
        {
            throw new InvalidOperationException($"[MCP025] 远程客户端 '{clientId}' 已注册");
        }

        client.NotificationReceived += (sender, args) => OnClientNotificationReceived(clientId, args);
        client.ConnectionLost += (sender, args) => OnClientConnectionLost(clientId, args);

        _logger.LogInformation("已注册远程 MCP 客户端: {ClientId}", clientId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注销远程 MCP 客户端
    /// </summary>
    public async Task<bool> UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        if (_remoteClients.TryGetValue(clientId, out var entry))
        {
            await entry.Client.DisposeAsync();
            _remoteClients.TryRemove(clientId, out _);
            _lastKnownToolSpecs.TryRemove(clientId, out _);

            if (_reconnectCtsMap.TryGetValue(clientId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _reconnectCtsMap.TryRemove(clientId, out _);
            }

            _logger.LogInformation("已移除远程 MCP 客户端: {ClientId}", clientId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取远程客户端（异步）
    /// </summary>
    public Task<IMcpClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        return Task.FromResult(_remoteClients.GetValueOrDefault(clientId)?.Client);
    }

    /// <summary>
    /// 获取所有远程客户端（异步）
    /// </summary>
    public Task<IReadOnlyDictionary<string, IMcpClient>> GetAllClientsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, IMcpClient>>(
            _remoteClients.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.Client));
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
        var (client, previousSpecs) = GetClientAndSpecs(clientId, cancellationToken);

        var ctx = new RemoteSyncContext
        {
            ClientId = clientId,
            Operation = RemoteSyncOperation.Tools,
            AcceptLevel = _acceptLevel,
            CancellationToken = cancellationToken,
            Client = client,
            PreviousToolSpecs = previousSpecs ?? [],
        };

        var pipeline = _syncPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.SyncedNames.Count > 0)
        {
            var newSpecs = ctx.ToolsResult?.GetData()
                .Select(t => new ToolSpec(
                    McpNameNormalizer.BuildMcpToolName(clientId, t.Name),
                    t.Description,
                    t.InputSchema?.ToString()))
                .ToList();
            if (newSpecs is not null)
            {
                UpdateToolSpecs(clientId, newSpecs, cancellationToken);
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
        var (client, previousSpecs) = GetClientAndSpecs(clientId, cancellationToken);

        if (client == null)
        {
            return new RemoteToolsSyncResult(false, Array.Empty<string>(), $"'{'\''}{clientId}{'\''} 未找到");
        }

        try
        {
            var toolsResult = await client.ListToolsAsync(cancellationToken);

            if (!toolsResult.Success)
            {
                return new RemoteToolsSyncResult(false, Array.Empty<string>(), toolsResult.ErrorMessage);
            }

            var newSpecs = toolsResult.GetData()
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

            var toolItems = toolsResult.GetData()
                .Select(tool =>
                {
                    var remoteToolHandler = new RemoteMcpToolDispatch(clientId, client, tool);
                    var fullToolName = McpNameNormalizer.BuildMcpToolName(clientId, tool.Name);
                    return (FullToolName: fullToolName, Handler: remoteToolHandler);
                })
                .ToList();

            await Task.WhenAll(toolItems.Select(item => _toolRegistry.RegisterToolAsync(item.Handler, cancellationToken))).ConfigureAwait(false);

            UpdateToolSpecs(clientId, newSpecs, cancellationToken);

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

    /// <summary>获取远程客户端和已知的工具规格</summary>
    private (IMcpClient? Client, List<ToolSpec>? PreviousSpecs) GetClientAndSpecs(string clientId, CancellationToken cancellationToken)
    {
        return (_remoteClients.GetValueOrDefault(clientId)?.Client,
            _lastKnownToolSpecs.TryGetValue(clientId, out var specs) ? specs : null);
    }

    /// <summary>更新远程客户端的工具规格缓存</summary>
    private void UpdateToolSpecs(string clientId, List<ToolSpec> specs, CancellationToken cancellationToken)
    {
        _lastKnownToolSpecs[clientId] = specs;
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
        var client = _remoteClients.GetValueOrDefault(clientId)?.Client;

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
        var client = _remoteClients.GetValueOrDefault(clientId)?.Client;

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

            var resourceUris = resourcesResult.GetData()
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
        var client = _remoteClients.GetValueOrDefault(clientId)?.Client;

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
        var client = _remoteClients.GetValueOrDefault(clientId)?.Client;

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

            var promptNames = promptsResult.GetData()
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
    public Task<int> GetClientCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_remoteClients.Count);
    }

    /// <summary>
    /// 清除所有远程客户端
    /// </summary>
    public async Task ClearAllClientsAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(_remoteClients.Values
            .Select(entry => entry.Client.DisposeAsync().AsTask()));

        _remoteClients.Clear();
        _lastKnownToolSpecs.Clear();
        _logger.LogInformation("所有远程 MCP 客户端已清除");
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
}
