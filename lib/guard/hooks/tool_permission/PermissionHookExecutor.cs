namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限 Hook 执行器 — 编排权限请求 Hook 的执行，转换 Hook 结果为权限决策
/// </summary>
[Register(typeof(IPermissionHookExecutor), ServiceLifetime.Singleton)]
public sealed partial class PermissionHookExecutor : ServiceEntity, IPermissionHookExecutor
{

    /// <summary>
    /// 构造权限 Hook 执行器
    /// </summary>
    public PermissionHookExecutor(IHookOrchestrator hookOrchestrator, ILogger<PermissionHookExecutor>? logger = null, ITelemetryService? telemetryService = null)
    {
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly IHookOrchestrator _hookOrchestrator;
    private readonly ILogger<PermissionHookExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />
    public Task RegisterHookAsync(IPermissionHook hook, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("权限钩子注册: {HookName}", hook.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterHookAsync(string hookName, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("权限钩子注销: {HookName}", hookName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PermissionHookResult> ExecuteHooksAsync(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode,
        List<PermissionUpdate>? suggestions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var eventSuggestions = suggestions?.Select(s => new JoinCode.Abstractions.Hooks.PermissionUpdate
        {
            ToolName = s.ToolName,
            Action = s.Action,
            Destination = s.Destination,
            Parameters = s.Parameters
        }).ToList();

        var hookInput = HookInputFactory.ForPermissionRequest(
            toolName,
            toolUseId,
            input,
            permissionMode,
            eventSuggestions);

        await foreach (var result in _hookOrchestrator.ExecuteHooksAsync(hookInput, cancellationToken))
        {
            if (result.PermissionRequestResult != null)
            {
                var convertedResult = ConvertToToolPermissionResult(result.PermissionRequestResult);
                _telemetryService?.RecordCount("permission.hook.count", new() { ["tool"] = toolName, ["behavior"] = convertedResult.Behavior.ToValue() }, description: "Permission hook count");

                yield return new PermissionHookResult
                {
                    HookName = "PermissionRequest",
                    PermissionRequestResult = convertedResult
                };

                if (result.PreventContinuation || result.Outcome == HookOutcome.Blocking)
                {
                    yield break;
                }
            }
        }
    }

    private static PermissionRequestResult ConvertToToolPermissionResult(JoinCode.Abstractions.Hooks.PermissionRequestResult result)
    {
        return result.Behavior switch
        {
            PermissionBehavior.Allow => PermissionRequestResult.Allow(
                result is JoinCode.Abstractions.Hooks.PermissionAllowResult allow ? allow.UpdatedInput : null,
                result is JoinCode.Abstractions.Hooks.PermissionAllowResult allow2
                    ? allow2.UpdatedPermissions?.Select(u => new PermissionUpdate
                    {
                        ToolName = u.ToolName,
                        Action = u.Action,
                        Destination = u.Destination,
                        Parameters = u.Parameters
                    }).ToList()
                    : null),
            _ => PermissionRequestResult.Deny(
                result is JoinCode.Abstractions.Hooks.PermissionDenyResult deny ? deny.Message ?? "Permission denied" : "Permission denied",
                false)
        };
    }

    /// <inheritdoc />
    public Task<int> GetRegisteredHookCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}

/// <summary>
/// 权限请求结果 — 表示 Hook 处理后的权限决策（允许/拒绝）及附带信息
/// </summary>
public sealed record PermissionRequestResult
{
    /// <summary>
    /// 决策行为类型
    /// </summary>
    public required PermissionBehavior Behavior { get; init; }

    /// <summary>
    /// 更新后的工具输入参数
    /// </summary>
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }

    /// <summary>
    /// 更新后的权限规则列表
    /// </summary>
    public List<PermissionUpdate>? UpdatedPermissions { get; init; }

    /// <summary>
    /// 附加消息（如拒绝原因）
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 是否中断后续处理
    /// </summary>
    public bool Interrupt { get; init; }

    /// <summary>
    /// 创建允许结果
    /// </summary>
    public static PermissionRequestResult Allow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null)
    {
        return new PermissionRequestResult
        {
            Behavior = PermissionBehavior.Allow,
            UpdatedInput = updatedInput,
            UpdatedPermissions = updatedPermissions
        };
    }

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static PermissionRequestResult Deny(string message, bool interrupt = false)
    {
        return new PermissionRequestResult
        {
            Behavior = PermissionBehavior.Deny,
            Message = message,
            Interrupt = interrupt
        };
    }
}

/// <summary>
/// 权限 Hook 执行结果 — 包含 Hook 名称和权限请求结果
/// </summary>
public sealed record PermissionHookResult
{
    /// <summary>
    /// Hook 名称
    /// </summary>
    public required string HookName { get; init; }

    /// <summary>
    /// 权限请求结果
    /// </summary>
    public PermissionRequestResult? PermissionRequestResult { get; init; }
}

/// <summary>
/// 权限 Hook 接口 — 自定义权限检查逻辑的扩展点
/// </summary>
public interface IPermissionHook
{
    /// <summary>
    /// Hook 名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行权限检查
    /// </summary>
    Task<PermissionHookResult?> ExecuteAsync(PermissionHookContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 权限 Hook 上下文 — 封装 Hook 执行所需的工具调用信息
/// </summary>
public sealed record PermissionHookContext
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具使用ID
    /// </summary>
    public required string ToolUseId { get; init; }

    /// <summary>
    /// 工具输入参数
    /// </summary>
    public required Dictionary<string, JsonElement> Input { get; init; }

    /// <summary>
    /// 权限模式
    /// </summary>
    public string? PermissionMode { get; init; }

    /// <summary>
    /// 建议的权限更新列表
    /// </summary>
    public List<PermissionUpdate>? Suggestions { get; init; }
}
