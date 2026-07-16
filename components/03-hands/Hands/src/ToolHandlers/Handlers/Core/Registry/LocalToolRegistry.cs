
namespace Tools;

[Register]
public sealed partial class LocalToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IToolHandler> _tools = new();
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

    public LocalToolRegistry( ILogger? logger)
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
            if (_tools.ContainsKey(handler.Name))
            {
                // 覆盖已注册的同名工具（增强版替代基础版）
                _tools[handler.Name] = handler;
                OnToolRegistered(handler.Name, handler.Description);
                _logger?.LogDebug("Tool re-registered (overwritten): {ToolName}", handler.Name);
                return;
            }

            _tools[handler.Name] = handler;
            OnToolRegistered(handler.Name, handler.Description);
            _logger?.LogDebug("Tool registered: {ToolName}", handler.Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        await RegisterToolAsync(new DelegateToolHandler(name, description, inputSchema, handler), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _tools.Remove(toolName);
            if (removed)
            {
                OnToolUnregistered(toolName);
                _logger?.LogDebug("Tool unregistered: {ToolName}", toolName);
            }
            return removed;
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
                    Content = new List<ToolContent>
                    {
                        new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' not found." }
                    },
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
                Content = new List<ToolContent>
                {
                    new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' was canceled." }
                },
                IsError = true
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = new List<ToolContent>
                {
                    new() { Type = ToolContentType.Text, Text = $"Error executing tool '{toolName}': {ex.Message}" }
                },
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
