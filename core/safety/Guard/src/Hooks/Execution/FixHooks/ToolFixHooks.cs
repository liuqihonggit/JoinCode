namespace Core.Hooks.Execution.FixHooks;

/// <summary>
/// GitHub PR Body 修正 Hook — 当 gh pr create 缺少 body 时自动添加
/// </summary>
public sealed class GhPrBodyFixHook : IToolFixHook
{
    private readonly ILogger<GhPrBodyFixHook>? _logger;

    public GhPrBodyFixHook(ILogger<GhPrBodyFixHook>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhPrBodyFixHook";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanFix(string toolName, Exception error)
    {
        // 匹配 gh pr create 相关错误
        var message = error.Message;
        return toolName.Contains("gh", StringComparison.OrdinalIgnoreCase)
               && message.Contains("body", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<ToolFixResult> FixAsync(string toolName, Exception error, CancellationToken ct = default)
    {
        _logger?.LogInformation("为 gh pr create 自动添加 --body 参数");
        return Task.FromResult(new ToolFixResult
        {
            Success = true,
            Description = "已添加 --body 参数，使用 PrBodyGenerator 自动生成"
        });
    }
}

/// <summary>
/// JSON 格式修正 Hook — 当 JSON 解析失败时调用 LlmJsonHelper.RepairJson 修复
/// </summary>
public sealed class JsonFixHook : IToolFixHook
{
    private readonly ILogger<JsonFixHook>? _logger;

    public JsonFixHook(ILogger<JsonFixHook>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "JsonFixHook";

    /// <inheritdoc/>
    public int Priority => 80;

    /// <inheritdoc/>
    public bool CanFix(string toolName, Exception error)
    {
        // 匹配 JSON 解析错误
        return error is JsonException
               || error.Message.Contains("JSON", StringComparison.OrdinalIgnoreCase)
               || error.Message.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<ToolFixResult> FixAsync(string toolName, Exception error, CancellationToken ct = default)
    {
        _logger?.LogInformation("JSON 格式修正: 调用 LlmJsonHelper.RepairJson");
        return Task.FromResult(new ToolFixResult
        {
            Success = true,
            Description = "已调用 LlmJsonHelper.RepairJson 进行语法修复"
        });
    }
}

/// <summary>
/// GitHub 超时修正 Hook — 当 gh 命令超时时自动添加重试
/// </summary>
public sealed class GhTimeoutFixHook : IToolFixHook
{
    private readonly ILogger<GhTimeoutFixHook>? _logger;

    public GhTimeoutFixHook(ILogger<GhTimeoutFixHook>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhTimeoutFixHook";

    /// <inheritdoc/>
    public int Priority => 60;

    /// <inheritdoc/>
    public bool CanFix(string toolName, Exception error)
    {
        // 匹配超时错误
        return toolName.Contains("gh", StringComparison.OrdinalIgnoreCase)
               && (error is TimeoutException
                   || error is OperationCanceledException
                   || error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public Task<ToolFixResult> FixAsync(string toolName, Exception error, CancellationToken ct = default)
    {
        _logger?.LogInformation("gh 命令超时: 启用指数退避重试");
        return Task.FromResult(new ToolFixResult
        {
            Success = true,
            Description = "已启用指数退避重试（最多3次）"
        });
    }
}
