
namespace Tools;

/// <summary>
/// 本地工具注册表，提供工具的注册、注销、查询与执行能力，并维护按工具种类和分组的多级索引。
/// </summary>
[Register(typeof(IToolRegistry), ServiceLifetime.Singleton)]
public sealed partial class LocalToolRegistry : IToolRegistry {
    private readonly ConcurrentDictionary<string, IToolHandler> _tools = new();
    private readonly ConcurrentDictionary<ToolKind, ConcurrentDictionary<string, IToolHandler>> _kindIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IToolHandler>> _groupIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>
    /// 工具注册或重新注册时触发的事件。
    /// </summary>
    public event EventHandler<ToolRegisteredEventArgs>? ToolRegistered;
    /// <summary>
    /// 工具注销时触发的事件。
    /// </summary>
    public event EventHandler<ToolUnregisteredEventArgs>? ToolUnregistered;
    /// <summary>
    /// 所有工具被清空时触发的事件。
    /// </summary>
    public event EventHandler? ToolsCleared;

    /// <summary>
    /// 初始化 <see cref="LocalToolRegistry"/> 的新实例，不使用日志记录器。
    /// </summary>
    public LocalToolRegistry() {

        _logger = null;
    }

    /// <summary>
    /// 初始化 <see cref="LocalToolRegistry"/> 的新实例，并指定可选的日志记录器。
    /// </summary>
    /// <param name="logger">用于记录诊断信息的日志记录器，可为 null。</param>
    public LocalToolRegistry(ILogger? logger) {

        _logger = logger;
    }

    /// <summary>
    /// 异步注册工具处理器。若已存在同名工具，则覆盖旧工具并更新索引。
    /// </summary>
    /// <param name="handler">要注册的工具处理器。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>表示异步注册操作的任务。</returns>
    public Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(handler);

        var isOverwrite = _tools.TryGetValue(handler.Name, out var old);
        if (isOverwrite && old is not null)
            RemoveFromIndex(old);

        _tools[handler.Name] = handler;
        AddToIndex(handler);

        OnToolRegistered(handler.Name, handler.Description);
        _logger?.LogDebug(isOverwrite ? "Tool re-registered (overwritten): {ToolName}" : "Tool registered: {ToolName}", handler.Name);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步注册工具，通过委托构造工具处理器并注册到注册表。
    /// </summary>
    /// <param name="name">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="inputSchema">工具输入参数的 JSON 模式。</param>
    /// <param name="handler">工具执行委托。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <param name="kind">工具种类，默认为 <see cref="ToolKind.System"/>。</param>
    /// <param name="groupName">工具所属分组名称，可为 null。</param>
    /// <param name="timeoutPolicy">工具超时策略，可为 null。</param>
    /// <param name="category">工具分类标识，可为 null。</param>
    /// <returns>表示异步注册操作的任务。</returns>
    public Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        return RegisterToolAsync(new DelegateToolHandler(name, description, inputSchema, handler, kind, groupName, timeoutPolicy, category), cancellationToken);
    }

    /// <summary>
    /// 异步批量注册工具处理器 — 单次操作注册多个工具，消除逐个注册的 async 状态机开销。
    /// </summary>
    /// <param name="handlers">要注册的工具处理器列表。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    public Task RegisterToolsBatchAsync(IReadOnlyList<IToolHandler> handlers, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(handlers);
        if (handlers.Count == 0) return Task.CompletedTask;

        for (var i = 0; i < handlers.Count; i++) {
            var handler = handlers[i];
            ArgumentNullException.ThrowIfNull(handler);

            if (_tools.TryGetValue(handler.Name, out var old))
                RemoveFromIndex(old);

            _tools[handler.Name] = handler;
            AddToIndex(handler);
            OnToolRegistered(handler.Name, handler.Description);
        }

        _logger?.LogDebug("Tools batch registered: {Count} tools", handlers.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步注销指定名称的工具。
    /// </summary>
    /// <param name="toolName">要注销的工具名称。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>若工具存在并已注销则返回 true，否则返回 false。</returns>
    public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        if (!_tools.TryRemove(toolName, out var handler))
            return Task.FromResult(false);

        RemoveFromIndex(handler);
        OnToolUnregistered(toolName);
        _logger?.LogDebug("Tool unregistered: {ToolName}", toolName);
        return Task.FromResult(true);

    }

    /// <summary>
    /// 异步获取指定名称的工具处理器。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>若找到则返回对应的工具处理器，否则返回 null。</returns>
    public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        return Task.FromResult(_tools.GetValueOrDefault(toolName));

    }

    /// <summary>
    /// 异步获取所有已注册工具的只读字典快照。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>以工具名称为键的工具处理器只读字典。</returns>
    public Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default) {

        return Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(_tools.ToFrozenDictionary());

    }

    /// <summary>
    /// 异步获取所有工具分组的名称集合。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>分组名称的不可变集合。</returns>
    public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default) {

        return Task.FromResult(_groupIndex.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase));

    }

    /// <summary>
    /// 异步获取指定种类的所有工具的只读字典快照。
    /// </summary>
    /// <param name="kind">工具种类。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>属于指定种类的工具处理器只读字典，若无则返回空字典。</returns>
    public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default) {

        return Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(_kindIndex.GetValueOrDefault(kind)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty);

    }

    /// <summary>
    /// 异步获取指定分组下的所有工具的只读字典快照。
    /// </summary>
    /// <param name="groupName">分组名称。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>属于指定分组的工具处理器只读字典，若无则返回空字典。</returns>
    public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(groupName);

        return Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(_groupIndex.GetValueOrDefault(groupName)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty);

    }

    /// <summary>
    /// 异步执行指定名称的工具，并传入参数与可选的进度回调。
    /// </summary>
    /// <param name="toolName">要执行的工具名称。</param>
    /// <param name="arguments">工具输入参数字典。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <param name="onProgress">可选的进度回调，可为 null。</param>
    /// <returns>工具执行结果；若工具不存在或执行出错，则返回包含错误信息的 <see cref="ToolResult"/>。</returns>
    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!_tools.TryGetValue(toolName, out var handler)) {
            return new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' not found." }],
                IsError = true
            };
        }

        try {
            return await handler.ExecuteAsync(arguments, cancellationToken, onProgress).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            return new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' was canceled." }],
                IsError = true
            };
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ToolExceptionDiagnosticHelper.BuildErrorResult(toolName, ex, null);
        }
    }

    /// <summary>
    /// 异步获取指定名称的工具元信息。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>若找到则返回工具元信息，否则返回 null。</returns>
    public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) {
        if (!_tools.TryGetValue(toolName, out var handler)) return Task.FromResult<ToolInfo?>(null);
        return Task.FromResult<ToolInfo?>(new ToolInfo {
            Name = handler.Name,
            Description = handler.Description,
            InputSchema = handler.InputSchema,
            Category = handler.Category,
            GroupName = handler.GroupName
        });
    }

    /// <summary>
    /// 异步获取所有已注册工具的元信息列表。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>所有工具的元信息只读列表。</returns>
    public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult<IReadOnlyList<ToolInfo>>(
            _tools.Select(kvp => new ToolInfo {
                Name = kvp.Value.Name,
                Description = kvp.Value.Description,
                InputSchema = kvp.Value.InputSchema,
                Category = kvp.Value.Category,
                GroupName = kvp.Value.GroupName
            }).ToList()
        );
    }

    /// <summary>
    /// 异步判断指定名称的工具是否已注册。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>若工具已注册则返回 true，否则返回 false。</returns>
    public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        return Task.FromResult(_tools.ContainsKey(toolName));

    }

    /// <summary>
    /// 异步获取已注册工具的数量。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>已注册工具的数量。</returns>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default) {

        return Task.FromResult(_tools.Count);

    }

    /// <summary>
    /// 异步清空所有已注册的工具及索引。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>表示异步清空操作的任务。</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default) {
        _tools.Clear();
        _kindIndex.Clear();
        _groupIndex.Clear();
        OnToolsCleared();
        _logger?.LogInformation("All tools cleared");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步释放注册表占用的资源。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _disposed, 1);
        return default;
    }

    private void AddToIndex(IToolHandler handler) {
        var kindBucket = _kindIndex.GetOrAdd(handler.Kind, _ => new ConcurrentDictionary<string, IToolHandler>());
        kindBucket[handler.Name] = handler;

        if (handler.GroupName is not null) {
            var groupBucket = _groupIndex.GetOrAdd(handler.GroupName, _ => new ConcurrentDictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase));
            groupBucket[handler.Name] = handler;
        }
    }

    private void RemoveFromIndex(IToolHandler handler) {
        if (_kindIndex.TryGetValue(handler.Kind, out var kindBucket))
            kindBucket.TryRemove(handler.Name, out _);

        if (handler.GroupName is not null && _groupIndex.TryGetValue(handler.GroupName, out var groupBucket))
            groupBucket.TryRemove(handler.Name, out _);
    }

    private void OnToolRegistered(string toolName, string description) {
        ToolRegistered?.Invoke(this, new ToolRegisteredEventArgs {
            ToolName = toolName,
            Description = description
        });
    }

    private void OnToolUnregistered(string toolName) {
        ToolUnregistered?.Invoke(this, new ToolUnregisteredEventArgs {
            ToolName = toolName
        });
    }

    private void OnToolsCleared() {
        ToolsCleared?.Invoke(this, EventArgs.Empty);
    }
}
