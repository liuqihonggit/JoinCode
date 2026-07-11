
namespace Core.Hooks.Session;

/// <summary>
/// 会话钩子管理器扩展接口 — Guard 内部使用的额外方法
/// </summary>
public interface ISessionHookManagerInternal : ISessionHookManager
{
    /// <summary>
    /// 获取会话钩子
    /// </summary>
    Task<List<SourcedHookConfig>> GetSessionHooksAsync(
        string sessionId,
        HookEvent? hookEvent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取会话函数钩子
    /// </summary>
    Task<List<FunctionHook>> GetSessionFunctionHooksAsync(
        string sessionId,
        HookEvent? hookEvent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除会话的所有钩子
    /// </summary>
    Task ClearSessionHooksAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 会话钩子条目
/// </summary>
public sealed record SessionHookEntry
{
    /// <summary>
    /// 钩子命令
    /// </summary>
    public required HookCommand Hook { get; init; }

    /// <summary>
    /// 匹配器
    /// </summary>
    public string? Matcher { get; init; }

    /// <summary>
    /// 技能根目录
    /// </summary>
    public string? SkillRoot { get; init; }

    /// <summary>
    /// 成功回调
    /// </summary>
    public Action<HookResult>? OnSuccess { get; init; }
}

/// <summary>
/// 会话钩子存储
/// </summary>
public sealed partial class SessionHookStore
{
    /// <summary>
    /// 按事件存储的钩子
    /// </summary>
    public ConcurrentDictionary<HookEvent, ConcurrentBag<SessionHookEntry>> Hooks { get; } = new();

    /// <summary>
    /// 添加钩子
    /// </summary>
    public void AddHook(HookEvent hookEvent, SessionHookEntry entry)
    {
        var bag = Hooks.GetOrAdd(hookEvent, _ => new ConcurrentBag<SessionHookEntry>());
        bag.Add(entry);
    }

    /// <summary>
    /// 移除钩子
    /// </summary>
    public void RemoveHook(HookEvent hookEvent, Func<SessionHookEntry, bool> predicate)
    {
        if (!Hooks.TryGetValue(hookEvent, out var bag))
        {
            return;
        }

        // ConcurrentBag 不支持直接移除，需要重新创建
        var newBag = new ConcurrentBag<SessionHookEntry>(
            bag.Where(e => !predicate(e)));

        Hooks[hookEvent] = newBag;
    }

    /// <summary>
    /// 获取事件的钩子
    /// </summary>
    public List<SessionHookEntry> GetHooks(HookEvent hookEvent)
    {
        if (!Hooks.TryGetValue(hookEvent, out var bag))
        {
            return new List<SessionHookEntry>();
        }

        return bag.ToList();
    }

    /// <summary>
    /// 获取所有钩子
    /// </summary>
    public Dictionary<HookEvent, List<SessionHookEntry>> GetAllHooks()
    {
        return Hooks.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList());
    }

    /// <summary>
    /// 清除所有钩子
    /// </summary>
    public void Clear()
    {
        Hooks.Clear();
    }
}

/// <summary>
/// 会话钩子管理器实现
/// </summary>
[Register]
public sealed partial class SessionHookManager : ISessionHookManagerInternal
{
    private readonly ConcurrentDictionary<string, SessionHookStore> _sessionStores = new();
    [Inject] private readonly ILogger<SessionHookManager>? _logger;

    public SessionHookManager(ILogger<SessionHookManager>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task AddSessionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        var store = _sessionStores.GetOrAdd(sessionId, _ => new SessionHookStore());

        store.AddHook(hookEvent, new SessionHookEntry
        {
            Hook = hook,
            Matcher = matcher
        });

        _logger?.LogDebug(
            "Added session hook for event {Event} in session {SessionId}: {HookDisplay}",
            hookEvent,
            sessionId,
            hook.GetDisplayText());

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> AddFunctionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        Func<HookInput, CancellationToken, Task<HookResult>> callback,
        string? errorMessage = null,
        int? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var hookId = $"function-hook-{Guid.NewGuid():N}";

        var functionHook = new FunctionHook
        {
            Id = hookId,
            Callback = callback,
            ErrorMessage = errorMessage,
            Timeout = timeout ?? 5
        };

        var store = _sessionStores.GetOrAdd(sessionId, _ => new SessionHookStore());

        store.AddHook(hookEvent, new SessionHookEntry
        {
            Hook = functionHook,
            Matcher = matcher
        });

        _logger?.LogDebug(
            "Added function hook {HookId} for event {Event} in session {SessionId}",
            hookId,
            hookEvent,
            sessionId);

        return Task.FromResult(hookId);
    }

    /// <inheritdoc />
    public Task RemoveFunctionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string hookId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionStores.TryGetValue(sessionId, out var store))
        {
            return Task.CompletedTask;
        }

        store.RemoveHook(hookEvent, entry =>
            entry.Hook is FunctionHook fh && fh.Id == hookId);

        _logger?.LogDebug(
            "Removed function hook {HookId} for event {Event} in session {SessionId}",
            hookId,
            hookEvent,
            sessionId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveSessionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionStores.TryGetValue(sessionId, out var store))
        {
            return Task.CompletedTask;
        }

        store.RemoveHook(hookEvent, entry =>
            entry.Matcher == matcher && entry.Hook.IsEqualTo(hook));

        _logger?.LogDebug(
            "Removed session hook for event {Event} in session {SessionId}",
            hookEvent,
            sessionId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<List<SourcedHookConfig>> GetSessionHooksAsync(
        string sessionId,
        HookEvent? hookEvent = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionStores.TryGetValue(sessionId, out var store))
        {
            return Task.FromResult(new List<SourcedHookConfig>());
        }

        var result = new List<SourcedHookConfig>();

        if (hookEvent.HasValue)
        {
            var entries = store.GetHooks(hookEvent.Value)
                .Where(e => e.Hook is not FunctionHook); // 函数钩子单独处理

            foreach (var entry in entries)
            {
                result.Add(new SourcedHookConfig
                {
                    Event = hookEvent.Value,
                    Matcher = entry.Matcher,
                    Command = entry.Hook,
                    Source = HookSource.SessionHook,
                    SkillRoot = entry.SkillRoot
                });
            }
        }
        else
        {
            foreach (var evt in store.GetAllHooks())
            {
                var entries = evt.Value
                    .Where(e => e.Hook is not FunctionHook);

                foreach (var entry in entries)
                {
                    result.Add(new SourcedHookConfig
                    {
                        Event = evt.Key,
                        Matcher = entry.Matcher,
                        Command = entry.Hook,
                        Source = HookSource.SessionHook,
                        SkillRoot = entry.SkillRoot
                    });
                }
            }
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<List<FunctionHook>> GetSessionFunctionHooksAsync(
        string sessionId,
        HookEvent? hookEvent = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionStores.TryGetValue(sessionId, out var store))
        {
            return Task.FromResult(new List<FunctionHook>());
        }

        var result = new List<FunctionHook>();

        if (hookEvent.HasValue)
        {
            var entries = store.GetHooks(hookEvent.Value)
                .Where(e => e.Hook is FunctionHook)
                .Select(e => (FunctionHook)e.Hook);

            result.AddRange(entries);
        }
        else
        {
            foreach (var evt in store.GetAllHooks())
            {
                var entries = evt.Value
                    .Where(e => e.Hook is FunctionHook)
                    .Select(e => (FunctionHook)e.Hook);

                result.AddRange(entries);
            }
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task ClearSessionHooksAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStores.TryRemove(sessionId, out var store))
        {
            store.Clear();
            _logger?.LogDebug("Cleared all hooks for session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有会话ID
    /// </summary>
    public IEnumerable<string> GetAllSessionIds()
    {
        return _sessionStores.Keys.ToList();
    }

    /// <summary>
    /// 清除所有会话钩子
    /// </summary>
    public void ClearAllSessions()
    {
        foreach (var store in _sessionStores.Values)
        {
            store.Clear();
        }

        _sessionStores.Clear();
        _logger?.LogDebug("Cleared all session hooks");
    }
}
