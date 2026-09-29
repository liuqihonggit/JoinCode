
namespace Core.Hooks.Session;

/// <summary>
/// 会话钩子管理器扩展接口 — Guard 内部使用的额外方法
/// </summary>
public interface ISessionHookManagerInternal : ISessionHookManager {
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
public sealed record SessionHookEntry {
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
public sealed partial class SessionHookStore {
    private volatile ImmutableHamT<HookEvent, ImmutableList<SessionHookEntry>> _hooks = ImmutableHamT<HookEvent, ImmutableList<SessionHookEntry>>.Empty;

    /// <summary>
    /// 添加钩子 — 手写 CAS 循环(对 volatile 字段用 Interlocked.CompareExchange,使 Roslyn IDE0044 识别 ref 写入)
    /// </summary>
    public void AddHook(HookEvent hookEvent, SessionHookEntry entry) {
        while (true) {
            var current = _hooks;
            var list = current.GetValueOrDefault(hookEvent) ?? ImmutableList<SessionHookEntry>.Empty;
            var updated = current.SetItem(hookEvent, list.Add(entry));
            if (Interlocked.CompareExchange(ref _hooks, updated, current) == current) return;
        }
    }

    /// <summary>
    /// 移除钩子 — 手写 CAS 循环
    /// </summary>
    public void RemoveHook(HookEvent hookEvent, Func<SessionHookEntry, bool> predicate) {
        while (true) {
            var current = _hooks;
            if (!current.TryGetValue(hookEvent, out var list)) return;
            var newList = list.RemoveAll(new Predicate<SessionHookEntry>(predicate));
            var updated = newList.IsEmpty ? current.Remove(hookEvent) : current.SetItem(hookEvent, newList);
            if (Interlocked.CompareExchange(ref _hooks, updated, current) == current) return;
        }
    }

    /// <summary>
    /// 获取事件的钩子 — 返回不可变引用,无需拷贝
    /// </summary>
    public IReadOnlyList<SessionHookEntry> GetHooks(HookEvent hookEvent) {
        return _hooks.GetValueOrDefault(hookEvent) ?? (IReadOnlyList<SessionHookEntry>)[];
    }

    /// <summary>
    /// 获取所有钩子 — 返回不可变引用视图,无需逐项拷贝
    /// </summary>
    public IReadOnlyDictionary<HookEvent, IReadOnlyList<SessionHookEntry>> GetAllHooks()
        => _hooks.ToImmutableHamT(kvp => kvp.Key, kvp => (IReadOnlyList<SessionHookEntry>)kvp.Value);

    /// <summary>
    /// 清除所有钩子 — CAS 循环整体替换为 Empty,与 AddHook/RemoveHook 同步模式一致
    /// <para>updater 永返 Empty,CAS 失败重试最终一定清空。手写循环使 Roslyn IDE0044 识别 ref 写入。</para>
    /// </summary>
    public void Clear() {
        while (true) {
            var current = _hooks;
            if (Interlocked.CompareExchange(ref _hooks, ImmutableHamT<HookEvent, ImmutableList<SessionHookEntry>>.Empty, current) == current) return;
        }
    }
}

/// <summary>
/// 会话钩子管理器实现
/// </summary>
[Register(typeof(ISessionHookManagerInternal), ServiceLifetime.Singleton)]
[Register(typeof(ISessionHookManager), ServiceLifetime.Singleton)]
public sealed partial class SessionHookManager : ServiceEntity, ISessionHookManagerInternal {
    /// <summary>
    /// 函数钩子默认超时（秒）— 与 TS 端 FunctionHook 默认值对齐
    /// </summary>
    internal const int DefaultFunctionHookTimeoutSeconds = 5;

    private ImmutableHamT<string, SessionHookStore> _sessionStores = ImmutableHamT<string, SessionHookStore>.Empty;
    private readonly ILogger<SessionHookManager>? _logger;

    /// <summary>
    /// 初始化会话钩子管理器实例
    /// </summary>
    public SessionHookManager(ILogger<SessionHookManager>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task AddSessionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(hook);

        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
            ImmutableInterlocked.Update(ref _sessionStores, d => d.ContainsKey(sessionId) ? d : d.Add(sessionId, new SessionHookStore()));
            store = Volatile.Read(ref _sessionStores)[sessionId];
        }

        store.AddHook(hookEvent, new SessionHookEntry {
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
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(callback);

        var hookId = $"function-hook-{Guid.NewGuid():N}";

        var functionHook = new FunctionHook {
            Id = hookId,
            Callback = callback,
            ErrorMessage = errorMessage,
            Timeout = timeout ?? DefaultFunctionHookTimeoutSeconds
        };

        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
            ImmutableInterlocked.Update(ref _sessionStores, d => d.ContainsKey(sessionId) ? d : d.Add(sessionId, new SessionHookStore()));
            store = Volatile.Read(ref _sessionStores)[sessionId];
        }

        store.AddHook(hookEvent, new SessionHookEntry {
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
        CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
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
        CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
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
        CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
            return Task.FromResult(new List<SourcedHookConfig>());
        }

        var result = new List<SourcedHookConfig>();

        if (hookEvent.HasValue) {
            var entries = store.GetHooks(hookEvent.Value)
                .Where(e => e.Hook is not FunctionHook); // 函数钩子单独处理

            foreach (var entry in entries) {
                result.Add(new SourcedHookConfig {
                    Event = hookEvent.Value,
                    Matcher = entry.Matcher,
                    Command = entry.Hook,
                    Source = HookSource.SessionHook,
                    SkillRoot = entry.SkillRoot
                });
            }
        } else {
            foreach (var evt in store.GetAllHooks()) {
                var entries = evt.Value
                    .Where(e => e.Hook is not FunctionHook);

                foreach (var entry in entries) {
                    result.Add(new SourcedHookConfig {
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
        CancellationToken cancellationToken = default) {
        if (!Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store)) {
            return Task.FromResult(new List<FunctionHook>());
        }

        var result = new List<FunctionHook>();

        if (hookEvent.HasValue) {
            var entries = store.GetHooks(hookEvent.Value)
                .Where(e => e.Hook is FunctionHook)
                .Select(e => (FunctionHook)e.Hook);

            result.AddRange(entries);
        } else {
            foreach (var evt in store.GetAllHooks()) {
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
        CancellationToken cancellationToken = default) {
        var hadStore = Volatile.Read(ref _sessionStores).TryGetValue(sessionId, out var store);
        ImmutableInterlocked.Update(ref _sessionStores, d => d.Remove(sessionId));
        if (hadStore) {
            store!.Clear();
            _logger?.LogDebug("Cleared all hooks for session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有会话ID的快照拷贝
    /// </summary>
    public string[] GetAllSessionIds() => Volatile.Read(ref _sessionStores).Keys.ToArray();

    /// <summary>
    /// 清除所有会话钩子
    /// </summary>
    public void ClearAllSessions() {
        foreach (var store in Volatile.Read(ref _sessionStores).Values) {
            store.Clear();
        }

        Volatile.Write(ref _sessionStores, ImmutableHamT<string, SessionHookStore>.Empty);
        _logger?.LogDebug("Cleared all session hooks");
    }
}