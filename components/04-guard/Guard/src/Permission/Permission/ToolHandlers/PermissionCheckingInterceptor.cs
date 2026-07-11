
namespace Core.Permission;

/// <summary>
/// 权限检查拦截器 - 在工具调用前进行权限验证
/// </summary>
[Register]
public sealed partial class PermissionCheckingInterceptor : IPermissionCheckingInterceptor, IDisposable
{
    private readonly IToolPermissionManager _permissionManager;
    [Inject] private readonly ILogger<PermissionCheckingInterceptor>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly IToolPermissionFilter? _toolPermissionFilter;
    private bool _disposed;

    /// <summary>
    /// 拦截器优先级（数值越大优先级越高）
    /// </summary>
    public int Priority => 200;

    /// <summary>
    /// 创建权限检查拦截器
    /// </summary>
    public PermissionCheckingInterceptor(
        IToolPermissionManager permissionManager,
        ILogger<PermissionCheckingInterceptor>? logger = null,
        IToolPermissionFilter? toolPermissionFilter = null,
        IClockService? clock = null)
    {
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _logger = logger;
        _toolPermissionFilter = toolPermissionFilter;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 在工具调用前拦截并检查权限
    /// </summary>
    public async Task<PermissionInterceptResult> OnBeforeToolInvokeAsync(
        ToolInvokeContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogDebug("拦截工具调用，开始权限检查: Tool={ToolName}, RequestId={RequestId}",
            context.ToolName, context.RequestId);

        try
        {
            if (_toolPermissionFilter != null && _toolPermissionFilter.IsToolDenied(context.ToolName))
            {
                _logger?.LogWarning("工具被拒绝规则过滤: Tool={ToolName}", context.ToolName);
                return PermissionInterceptResult.Denied($"工具 '{context.ToolName}' 被拒绝规则过滤");
            }

            var request = new PermissionRequest(
                context.ToolName,
                context.Arguments,
                context.RequestId,
                _clock.GetUtcNowOffset());

            var result = await _permissionManager.CheckPermissionAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsGranted && !result.IsExpired)
            {
                _logger?.LogInformation("工具调用权限已批准: Tool={ToolName}, RequestId={RequestId}",
                    context.ToolName, context.RequestId);

                return PermissionInterceptResult.Allowed();
            }

            if (result.RequiresConfirmation)
            {
                _logger?.LogInformation("工具调用需要确认: Tool={ToolName}, RequestId={RequestId}, Prompt={Prompt}",
                    context.ToolName, context.RequestId, result.ConfirmationPrompt);

                return PermissionInterceptResult.ConfirmationRequired(result.ConfirmationPrompt ?? "需要确认");
            }

            var denyReason = result.DenyReason ?? "权限被拒绝";
            _logger?.LogWarning("工具调用权限被拒绝: Tool={ToolName}, RequestId={RequestId}, Reason={Reason}",
                context.ToolName, context.RequestId, denyReason);

            return PermissionInterceptResult.Denied(denyReason);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("权限检查被取消: Tool={ToolName}, RequestId={RequestId}",
                context.ToolName, context.RequestId);
            throw;
        }
        catch (PermissionDeniedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "权限检查时发生异常: Tool={ToolName}, RequestId={RequestId}",
                context.ToolName, context.RequestId);

            return PermissionInterceptResult.Denied($"权限检查失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 在工具调用后执行（可用于记录审计日志）
    /// </summary>
    public Task OnAfterToolInvokeAsync(
        ToolInvokeContext context,
        ToolInvokeResult invokeResult,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (invokeResult.Success)
        {
            _logger?.LogDebug("工具调用成功: Tool={ToolName}, RequestId={RequestId}",
                context.ToolName, context.RequestId);
        }
        else
        {
            _logger?.LogWarning("工具调用失败: Tool={ToolName}, RequestId={RequestId}, Error={Error}",
                context.ToolName, context.RequestId, invokeResult.ErrorMessage);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 检查权限并在被拒绝时抛出异常
    /// </summary>
    public async Task CheckPermissionOrThrowAsync(
        ToolInvokeContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await OnBeforeToolInvokeAsync(context, cancellationToken).ConfigureAwait(false);

        if (result.IsDenied)
        {
            throw PermissionDeniedException.ToolDenied(
                context.ToolName,
                result.DenyReason ?? "权限被拒绝");
        }

        if (result.RequiresConfirmation)
        {
            throw new PermissionPendingConfirmationException(
                context.ToolName,
                result.ConfirmationPrompt ?? "需要确认",
                context.RequestId);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 工具调用结果
/// </summary>
public sealed partial class ToolInvokeResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 返回数据
    /// </summary>
    public object? Data { get; }

    private ToolInvokeResult(bool success, string? errorMessage, object? data)
    {
        Success = success;
        ErrorMessage = errorMessage;
        Data = data;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static ToolInvokeResult SuccessResult(object? data = null) => new(true, null, data);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static ToolInvokeResult FailureResult(string errorMessage) => new(false, errorMessage, null);
}

/// <summary>
/// 权限拦截结果
/// </summary>
public sealed partial class PermissionInterceptResult
{
    /// <summary>
    /// 是否允许执行
    /// </summary>
    public bool IsAllowed { get; private set; }

    /// <summary>
    /// 是否被拒绝
    /// </summary>
    public bool IsDenied => !IsAllowed && !RequiresConfirmation;

    /// <summary>
    /// 是否需要确认
    /// </summary>
    public bool RequiresConfirmation { get; private set; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string? DenyReason { get; private set; }

    /// <summary>
    /// 确认提示信息
    /// </summary>
    public string? ConfirmationPrompt { get; private set; }

    private PermissionInterceptResult() { }

    /// <summary>
    /// 创建允许执行的结果
    /// </summary>
    public static PermissionInterceptResult Allowed() => new() { IsAllowed = true };

    /// <summary>
    /// 创建拒绝执行的结果
    /// </summary>
    public static PermissionInterceptResult Denied(string reason) => new() { DenyReason = reason };

    /// <summary>
    /// 创建需要确认的结果
    /// </summary>
    public static PermissionInterceptResult ConfirmationRequired(string prompt) => new() { RequiresConfirmation = true, ConfirmationPrompt = prompt };
}
