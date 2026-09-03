
namespace Tools;

[Register(typeof(IToolRegistry), ServiceLifetime.Singleton)]
public sealed partial class LocalToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IToolHandler> _tools = new();
    private readonly Dictionary<ToolKind, Dictionary<string, IToolHandler>> _kindIndex = new();
    private readonly Dictionary<string, Dictionary<string, IToolHandler>> _groupIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLock _lock = new();
    private readonly ILogger? _logger;

    public event EventHandler<ToolRegisteredEventArgs>? ToolRegistered;
    public event EventHandler<ToolUnregisteredEventArgs>? ToolUnregistered;
    public event EventHandler? ToolsCleared;

    public LocalToolRegistry()
    {

        _logger = null;
    }

    public LocalToolRegistry(ILogger? logger)
    {

        _logger = logger;
    }

    public async Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        var isOverwrite = _tools.ContainsKey(handler.Name);

        if (isOverwrite)
        {
            var old = _tools[handler.Name];
            RemoveFromIndex(old);
        }

        _tools[handler.Name] = handler;
        AddToIndex(handler);

        OnToolRegistered(handler.Name, handler.Description);
        _logger?.LogDebug(isOverwrite ? "Tool re-registered (overwritten): {ToolName}" : "Tool registered: {ToolName}", handler.Name);
    
    }

    public async Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        await RegisterToolAsync(new DelegateToolHandler(name, description, inputSchema, handler, kind, groupName, timeoutPolicy, category), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (!_tools.Remove(toolName, out var handler))
            return false;

        RemoveFromIndex(handler);
        OnToolUnregistered(toolName);
        _logger?.LogDebug("Tool unregistered: {ToolName}", toolName);
        return true;
    
    }

    public async Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _tools.GetValueOrDefault(toolName);
    
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _tools.ToFrozenDictionary();
    
    }

    public async Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _groupIndex.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _kindIndex.GetValueOrDefault(kind)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty;
    
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupName);

        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _groupIndex.GetValueOrDefault(groupName)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty;
    
    }

    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        IToolHandler? handler;
        using (var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_tools.TryGetValue(toolName, out handler))
            {
                return new ToolResult
                {
                    Content = [new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' not found." }],
                    IsError = true
                };
            }
        }

        try
        {
            return await handler.ExecuteAsync(arguments, cancellationToken, onProgress).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ToolResult
            {
                Content = [new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' was canceled." }],
                IsError = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult(toolName, ex, null);
        }
    }

    public async Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default)
    {
        var handler = await GetToolAsync(toolName, cancellationToken).ConfigureAwait(false);
        if (handler == null) return null;
        return new ToolInfo
        {
            Name = handler.Name,
            Description = handler.Description,
            InputSchema = handler.InputSchema,
            Category = handler.Category,
            GroupName = handler.GroupName
        };
    }

    public async Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default)
    {
        return (await GetAllToolsAsync(cancellationToken).ConfigureAwait(false))
            .Select(kvp => new ToolInfo
            {
                Name = kvp.Value.Name,
                Description = kvp.Value.Description,
                InputSchema = kvp.Value.InputSchema,
                Category = kvp.Value.Category,
                GroupName = kvp.Value.GroupName
            })
            .ToList();
    }

    public async Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _tools.ContainsKey(toolName);
    
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        return _tools.Count;
    
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        _tools.Clear();
        _kindIndex.Clear();
        _groupIndex.Clear();
        OnToolsCleared();
        _logger?.LogInformation("All tools cleared");
    
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void AddToIndex(IToolHandler handler)
    {
        if (!_kindIndex.TryGetValue(handler.Kind, out var kindBucket))
        {
            kindBucket = new Dictionary<string, IToolHandler>();
            _kindIndex[handler.Kind] = kindBucket;
        }
        kindBucket[handler.Name] = handler;

        if (handler.GroupName is not null)
        {
            if (!_groupIndex.TryGetValue(handler.GroupName, out var groupBucket))
            {
                groupBucket = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
                _groupIndex[handler.GroupName] = groupBucket;
            }
            groupBucket[handler.Name] = handler;
        }
    }

    private void RemoveFromIndex(IToolHandler handler)
    {
        if (_kindIndex.TryGetValue(handler.Kind, out var kindBucket))
            kindBucket.Remove(handler.Name);

        if (handler.GroupName is not null && _groupIndex.TryGetValue(handler.GroupName, out var groupBucket))
            groupBucket.Remove(handler.Name);
    }

    private void OnToolRegistered(string toolName, string description)
    {
        ToolRegistered?.Invoke(this, new ToolRegisteredEventArgs
        {
            ToolName = toolName,
            Description = description
        });
    }

    private void OnToolUnregistered(string toolName)
    {
        ToolUnregistered?.Invoke(this, new ToolUnregisteredEventArgs
        {
            ToolName = toolName
        });
    }

    private void OnToolsCleared()
    {
        ToolsCleared?.Invoke(this, EventArgs.Empty);
    }
}
