
namespace Tools;

[Register]
public sealed partial class LocalToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IToolHandler> _tools = new();
    private readonly Dictionary<ToolKind, Dictionary<string, IToolHandler>> _kindIndex = new();
    private readonly Dictionary<string, Dictionary<string, IToolHandler>> _groupIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock;
    private readonly ILogger? _logger;

    public event EventHandler<ToolRegisteredEventArgs>? ToolRegistered;
    public event EventHandler<ToolUnregisteredEventArgs>? ToolUnregistered;
    public event EventHandler? ToolsCleared;

    public LocalToolRegistry()
    {
        _lock = new SemaphoreSlim(1, 1);
        _logger = null;
    }

    public LocalToolRegistry(ILogger? logger)
    {
        _lock = new SemaphoreSlim(1, 1);
        _logger = logger;
    }

    public async Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        await RegisterToolAsync(new DelegateToolHandler(name, description, inputSchema, handler, kind, groupName), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_tools.Remove(toolName, out var handler))
                return false;

            RemoveFromIndex(handler);
            OnToolUnregistered(toolName);
            _logger?.LogDebug("Tool unregistered: {ToolName}", toolName);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tools.GetValueOrDefault(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tools.ToFrozenDictionary();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _groupIndex.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _kindIndex.GetValueOrDefault(kind)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _groupIndex.GetValueOrDefault(groupName)?.ToFrozenDictionary() ?? FrozenDictionary<string, IToolHandler>.Empty;
        }
        finally
        {
            _lock.Release();
        }
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
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            _lock.Release();
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
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = [new() { Type = ToolContentType.Text, Text = $"Error executing tool '{toolName}': {ex.Message}" }],
                IsError = true
            };
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
            InputSchema = handler.InputSchema
        };
    }

    public async Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default)
    {
        return (await GetAllToolsAsync(cancellationToken).ConfigureAwait(false))
            .Select(kvp => new ToolInfo
            {
                Name = kvp.Value.Name,
                Description = kvp.Value.Description,
                InputSchema = kvp.Value.InputSchema
            })
            .ToList();
    }

    public async Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tools.ContainsKey(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _tools.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _tools.Clear();
            _kindIndex.Clear();
            _groupIndex.Clear();
            OnToolsCleared();
            _logger?.LogInformation("All tools cleared");
        }
        finally
        {
            _lock.Release();
        }
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
