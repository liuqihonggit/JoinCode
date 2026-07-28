
namespace Core.Hooks;

/// <summary>
/// 异步钩子注册表
/// 管理异步执行的钩子进程
/// </summary>
public interface IAsyncHookRegistry
{
    /// <summary>
    /// 注册异步钩子
    /// </summary>
    void Register(PendingAsyncHook hook);

    /// <summary>
    /// 检查异步钩子响应
    /// </summary>
    Task<List<AsyncHookResponse>> CheckForResponsesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有待处理的钩子
    /// </summary>
    IReadOnlyList<PendingAsyncHook> GetPendingHooks();

    /// <summary>
    /// 移除已交付的钩子
    /// </summary>
    void RemoveDeliveredHooks(IEnumerable<string> processIds);

    /// <summary>
    /// 完成所有待处理的钩子
    /// </summary>
    Task FinalizeAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除所有钩子
    /// </summary>
    void ClearAll();
}

/// <summary>
/// 待处理的异步钩子
/// </summary>
public sealed record PendingAsyncHook
{
    /// <summary>
    /// 进程ID
    /// </summary>
    public required string ProcessId { get; init; }

    /// <summary>
    /// 钩子ID
    /// </summary>
    public required string HookId { get; init; }

    /// <summary>
    /// 钩子名称
    /// </summary>
    public required string HookName { get; init; }

    /// <summary>
    /// 钩子事件
    /// </summary>
    public required HookEvent HookEvent { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 插件ID
    /// </summary>
    public string? PluginId { get; init; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 执行的命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 响应是否已发送
    /// </summary>
    public bool ResponseAttachmentSent { get; set; }

    /// <summary>
    /// 进程对象
    /// </summary>
    public IAsyncHookProcess? Process { get; init; }

    /// <summary>
    /// 停止进度报告的回调
    /// </summary>
    public Action? StopProgressInterval { get; init; }

    /// <summary>
    /// 检查是否已超时
    /// </summary>
    public bool IsTimedOut => DateTimeOffset.UtcNow - StartTime > Timeout;
}

/// <summary>
/// 异步钩子进程接口
/// </summary>
public interface IAsyncHookProcess
{
    /// <summary>
    /// 进程状态
    /// </summary>
    AsyncHookProcessStatus Status { get; }

    /// <summary>
    /// 退出码
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// 获取标准输出
    /// </summary>
    Task<string> GetStdoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取标准错误
    /// </summary>
    Task<string> GetStderrAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 终止进程
    /// </summary>
    void Kill();

    /// <summary>
    /// 清理资源
    /// </summary>
    void Cleanup();
}

/// <summary>
/// 异步钩子进程状态
/// </summary>
public enum AsyncHookProcessStatus
{
    [EnumValue("running")] Running,
    [EnumValue("completed")] Completed,
    [EnumValue("killed")] Killed
}

/// <summary>
/// 异步钩子响应
/// </summary>
public sealed record AsyncHookResponse
{
    /// <summary>
    /// 进程ID
    /// </summary>
    public required string ProcessId { get; init; }

    /// <summary>
    /// 响应数据
    /// </summary>
    public Dictionary<string, JsonElement>? Response { get; init; }

    /// <summary>
    /// 钩子名称
    /// </summary>
    public required string HookName { get; init; }

    /// <summary>
    /// 钩子事件
    /// </summary>
    public required HookEvent HookEvent { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 插件ID
    /// </summary>
    public string? PluginId { get; init; }

    /// <summary>
    /// 标准输出
    /// </summary>
    public string? Stdout { get; init; }

    /// <summary>
    /// 标准错误
    /// </summary>
    public string? Stderr { get; init; }

    /// <summary>
    /// 退出码
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// 是否为会话开始事件
    /// </summary>
    public bool IsSessionStart => HookEvent == HookEvent.SessionStart;
}

/// <summary>
/// 异步钩子注册表实现
/// </summary>
[Register]
public sealed partial class AsyncHookRegistry : IAsyncHookRegistry
{
    private readonly ConcurrentDictionary<string, PendingAsyncHook> _pendingHooks = new();
    [Inject] private readonly ILogger<AsyncHookRegistry>? _logger;

    public AsyncHookRegistry(ILogger<AsyncHookRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Register(PendingAsyncHook hook)
    {
        _pendingHooks[hook.ProcessId] = hook;
        _logger?.LogDebug(
            "Registered async hook {ProcessId} ({HookName}) with timeout {TimeoutMs}ms",
            hook.ProcessId,
            hook.HookName,
            hook.Timeout.TotalMilliseconds);
    }

    /// <inheritdoc />
    public async Task<List<AsyncHookResponse>> CheckForResponsesAsync(CancellationToken cancellationToken = default)
    {
        var responses = new List<AsyncHookResponse>();
        var toRemove = new List<string>();

        var hooks = _pendingHooks.Values.ToList();
        _logger?.LogDebug("Checking {Count} async hooks for responses", hooks.Count);

        foreach (var hook in hooks)
        {
            try
            {
                if (await ProcessHookAsync(hook, responses, cancellationToken).ConfigureAwait(false))
                {
                    toRemove.Add(hook.ProcessId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process async hook {ProcessId}", hook.ProcessId);
                toRemove.Add(hook.ProcessId);
            }
        }

        // 清理已处理的钩子
        foreach (var processId in toRemove)
        {
            if (_pendingHooks.TryRemove(processId, out var removed))
            {
                removed.StopProgressInterval?.Invoke();
                removed.Process?.Cleanup();
            }
        }

        return responses;
    }

    private async Task<bool> ProcessHookAsync(
        PendingAsyncHook hook,
        List<AsyncHookResponse> responses,
        CancellationToken cancellationToken)
    {
        var process = hook.Process;
        if (process == null)
        {
            _logger?.LogDebug("Hook {ProcessId} has no process, removing", hook.ProcessId);
            return true;
        }

        // 检查超时
        if (hook.IsTimedOut && process.Status == AsyncHookProcessStatus.Running)
        {
            _logger?.LogDebug("Hook {ProcessId} timed out, killing", hook.ProcessId);
            process.Kill();
        }

        // 检查状态
        if (process.Status == AsyncHookProcessStatus.Killed)
        {
            _logger?.LogDebug("Hook {ProcessId} was killed, removing", hook.ProcessId);
            return true;
        }

        if (process.Status != AsyncHookProcessStatus.Completed)
        {
            return false;
        }

        // 已处理过或没有输出
        if (hook.ResponseAttachmentSent)
        {
            _logger?.LogDebug("Hook {ProcessId} already processed, removing", hook.ProcessId);
            return true;
        }

        var stdout = await process.GetStdoutAsync().ConfigureAwait(false);
        var stderr = await process.GetStderrAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(stdout))
        {
            _logger?.LogDebug("Hook {ProcessId} has no stdout, removing", hook.ProcessId);
            return true;
        }

        // 解析 JSON 响应
        var response = ParseResponse(stdout);

        hook.ResponseAttachmentSent = true;

        responses.Add(new AsyncHookResponse
        {
            ProcessId = hook.ProcessId,
            Response = response,
            HookName = hook.HookName,
            HookEvent = hook.HookEvent,
            ToolName = hook.ToolName,
            PluginId = hook.PluginId,
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = process.ExitCode
        });

        _logger?.LogDebug(
            "Hook {ProcessId} completed with response",
            hook.ProcessId);

        return true;
    }

    private Dictionary<string, JsonElement>? ParseResponse(string stdout)
    {
        var lines = stdout.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    return doc.RootElement.ToDictionary();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Failed to parse JSON output from async hook: {ex.Message}");
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingAsyncHook> GetPendingHooks()
    {
        return _pendingHooks.Values
            .Where(h => !h.ResponseAttachmentSent)
            .ToList();
    }

    /// <inheritdoc />
    public void RemoveDeliveredHooks(IEnumerable<string> processIds)
    {
        foreach (var processId in processIds)
        {
            if (_pendingHooks.TryRemove(processId, out var hook))
            {
                hook.StopProgressInterval?.Invoke();
                _logger?.LogDebug("Removed delivered hook {ProcessId}", processId);
            }
        }
    }

    /// <inheritdoc />
    public async Task FinalizeAllAsync(CancellationToken cancellationToken = default)
    {
        var hooks = _pendingHooks.Values.ToList();

        // 使用 LINQ 和 Task.WhenAll 替代 Parallel.ForEachAsync
        var tasks = hooks
            .Select(async hook =>
            {
                try
                {
                    if (hook.Process?.Status == AsyncHookProcessStatus.Completed)
                    {
                        var stdout = await hook.Process.GetStdoutAsync().ConfigureAwait(false);
                        var stderr = await hook.Process.GetStderrAsync().ConfigureAwait(false);

                        _logger?.LogDebug(
                            "Finalized hook {ProcessId}: exit={ExitCode}, stdout={StdoutLength}, stderr={StderrLength}",
                            hook.ProcessId,
                            hook.Process.ExitCode,
                            stdout.Length,
                            stderr.Length);
                    }
                    else if (hook.Process?.Status == AsyncHookProcessStatus.Running)
                    {
                        hook.Process.Kill();
                        _logger?.LogDebug("Killed running hook {ProcessId}", hook.ProcessId);
                    }

                    hook.StopProgressInterval?.Invoke();
                    hook.Process?.Cleanup();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to finalize hook {ProcessId}", hook.ProcessId);
                }
            })
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _pendingHooks.Clear();
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        foreach (var hook in _pendingHooks.Values)
        {
            try
            {
                hook.StopProgressInterval?.Invoke();
                hook.Process?.Kill();
                hook.Process?.Cleanup();
            }
            catch (Exception ex) { /* Ignore cleanup errors */
                System.Diagnostics.Trace.WriteLine($"Failed to cleanup hook: {ex.Message}");
            }
        }

        _pendingHooks.Clear();
        _logger?.LogDebug("Cleared all async hooks");
    }
}
