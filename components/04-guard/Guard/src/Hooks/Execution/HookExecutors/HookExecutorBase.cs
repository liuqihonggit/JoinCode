
namespace Core.Hooks.Execution;

/// <summary>
/// 钩子执行器基类
/// </summary>
public abstract class HookExecutorBase<THook> : IHookExecutor<THook> where THook : HookCommand
{
    protected readonly ILogger? Logger;

    protected HookExecutorBase(ILogger? logger = null)
    {
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string SupportedType { get; }

    /// <inheritdoc />
    public Task<HookResult> ExecuteAsync(
        HookCommand hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        if (hook is not THook typedHook)
        {
            throw new ArgumentException(
                $"Expected hook of type {typeof(THook).Name}, got {hook.GetType().Name}");
        }

        return ExecuteTypedAsync(typedHook, input, cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<HookResult> ExecuteTypedAsync(
        THook hook,
        HookInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建执行上下文
    /// </summary>
    protected HookExecutionContext CreateContext(THook hook, HookInput input)
    {
        return new HookExecutionContext
        {
            Input = input,
            Command = hook,
            Timeout = hook.Timeout.HasValue
                ? TimeSpan.FromSeconds(hook.Timeout.Value)
                : null
        };
    }

    /// <summary>
    /// 准备钩子输入 JSON
    /// </summary>
    protected string PrepareInputJson(HookInput input)
    {
        return JsonSerializer.Serialize(input.Payload, HooksJsonContext.Default.DictionaryStringJsonElement);
    }

    /// <summary>
    /// 替换参数占位符
    /// </summary>
    protected string SubstituteArguments(string template, string jsonInput)
    {
        var result = template.Replace("$ARGUMENTS", jsonInput);

        try
        {
            var node = JsonNode.Parse(jsonInput);
            if (node is JsonArray arr)
            {
                var index = 0;
                foreach (var element in arr)
                {
                    var value = element?.ToString() ?? "";
                    result = result.Replace($"${index}", value);
                    result = result.Replace($"$ARGUMENTS[{index}]", value);
                    index++;
                }
            }
            else if (node is JsonObject obj)
            {
                foreach (var property in obj)
                {
                    var value = property.Value?.ToString() ?? "";
                    result = result.Replace($"${property.Key}", value);
                    result = result.Replace($"$ARGUMENTS.{property.Key}", value);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to substitute template variables: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 使用超时执行操作
    /// </summary>
    protected async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan? timeout,
        string hookName,
        CancellationToken cancellationToken)
    {
        if (!timeout.HasValue)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, timeout.Value);

        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HookTimeoutException(hookName, timeout.Value);
        }
    }

    /// <summary>
    /// 解析钩子输出
    /// </summary>
    protected HookResult ParseHookOutput(
        string stdout,
        string stderr,
        int exitCode,
        string command,
        bool isAsync = false)
    {
        // 异步钩子只返回接受状态
        if (isAsync)
        {
            return HookResult.Success();
        }

        // 退出码 0 - 成功
        if (exitCode == 0)
        {
            // 尝试从 stdout 解析 JSON 响应
            var jsonLine = stdout.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith('{') && l.EndsWith('}'));

            if (jsonLine != null)
            {
                try
                {
                    return ParseJsonResponse(jsonLine, stdout, stderr);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to parse hook JSON response");
                }
            }

            return HookResult.Success(message: stdout);
        }

        // 退出码 2 - 阻塞错误（显示给模型）
        if (exitCode == 2)
        {
            return HookResult.Blocking(
                error: stderr ?? stdout,
                command: command,
                message: stderr);
        }

        // 其他退出码 - 非阻塞错误（只显示给用户）
        return HookResult.NonBlockingError(
            error: stderr ?? stdout,
            message: stderr);
    }

    /// <summary>
    /// 解析 JSON 响应
    /// </summary>
    protected virtual HookResult ParseJsonResponse(string jsonLine, string stdout, string stderr)
    {
        var hookDecision = JsonSerializer.Deserialize(jsonLine, HooksJsonContext.Default.HookDecision);
        if (hookDecision is null)
            return new HookResult { Outcome = HookOutcome.Success };

        var outcome = HookOutcome.Success;
        var preventContinuation = false;

        if (hookDecision.Continue.HasValue)
            preventContinuation = !hookDecision.Continue.Value;

        if (hookDecision.Decision?.ToLowerInvariant() == PermissionBehaviorConstants.Block)
        {
            outcome = HookOutcome.Blocking;
            preventContinuation = true;
        }

        var result = new HookResult
        {
            Outcome = outcome,
            PreventContinuation = preventContinuation,
            StopReason = hookDecision.StopReason,
            Message = hookDecision.Reason
        };

        var node = JsonNode.Parse(jsonLine);
        if (node?["hookSpecificOutput"] is JsonObject specificOutputObj)
        {
            result = ParseHookSpecificOutput(specificOutputObj, result);
        }

        return result;
    }

    /// <summary>
    /// 解析特定于事件的输出
    /// </summary>
    protected virtual HookResult ParseHookSpecificOutput(JsonObject specificOutput, HookResult result)
    {
        // 子类可以重写以处理特定事件类型的输出
        return result;
    }

    /// <summary>
    /// 记录执行日志
    /// </summary>
    protected void LogExecutionStart(THook hook, HookInput input)
    {
        Logger?.LogDebug(
            "Executing {HookType} hook for event {Event}: {HookDisplay}",
            SupportedType,
            input.Event,
            hook.GetDisplayText());
    }

    /// <summary>
    /// 记录执行完成
    /// </summary>
    protected void LogExecutionComplete(THook hook, HookResult result, TimeSpan duration)
    {
        Logger?.LogDebug(
            "Hook {HookType} completed in {DurationMs}ms with outcome {Outcome}",
            SupportedType,
            duration.TotalMilliseconds,
            result.Outcome);
    }

    private static readonly Regex JsonCodeBlockRegex = new(@"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
    private static readonly Regex JsonObjectRegex = new(@"\{[\s\S]*?\}", RegexOptions.Singleline);

    /// <summary>
    /// 从 LLM 响应中提取 JSON 内容
    /// </summary>
    protected static string? ExtractJsonFromResponse(string? response)
    {
        if (string.IsNullOrEmpty(response)) return null;

        var codeBlockMatch = JsonCodeBlockRegex.Match(response);
        if (codeBlockMatch.Success)
            return codeBlockMatch.Groups[1].Value.Trim();

        var jsonMatch = JsonObjectRegex.Match(response);
        return jsonMatch.Success ? jsonMatch.Value : null;
    }
}

/// <summary>
/// 钩子执行器工厂
/// </summary>
public interface IHookExecutorFactory
{
    /// <summary>
    /// 获取执行器
    /// </summary>
    IHookExecutor GetExecutor(string hookType);

    /// <summary>
    /// 注册执行器
    /// </summary>
    void RegisterExecutor(IHookExecutor executor);
}

/// <summary>
/// 钩子执行器工厂实现
/// </summary>
public partial class HookExecutorFactory : IHookExecutorFactory
{
    private readonly Dictionary<string, IHookExecutor> _executors = new();
    [Inject] private readonly ILogger<HookExecutorFactory>? _logger;

    public HookExecutorFactory(ILogger<HookExecutorFactory>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IHookExecutor GetExecutor(string hookType)
    {
        if (_executors.TryGetValue(hookType, out var executor))
        {
            return executor;
        }

        throw new NotSupportedException($"No executor registered for hook type: {hookType}");
    }

    /// <inheritdoc />
    public void RegisterExecutor(IHookExecutor executor)
    {
        _executors[executor.SupportedType] = executor;
        _logger?.LogDebug("Registered hook executor for type: {HookType}", executor.SupportedType);
    }
}
