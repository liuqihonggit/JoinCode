
namespace Core.Hooks.Execution;

/// <summary>
/// 函数回调钩子执行器
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed partial class FunctionHookExecutor : HookExecutorBase<FunctionHook>
{
    public FunctionHookExecutor(ILogger<FunctionHookExecutor>? logger = null)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Function;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        FunctionHook hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        LogExecutionStart(hook, input);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var context = CreateContext(hook, input);

            var result = await ExecuteWithTimeoutAsync(
                ct => hook.Callback(input, ct),
                context.Timeout,
                hook.Id,
                cancellationToken).ConfigureAwait(false);

            // 如果执行成功但结果为 null，返回空成功
            if (result == null)
            {
                result = HookResult.Success();
            }

            LogExecutionComplete(hook, result, stopwatch.Elapsed);
            return result;
        }
        catch (HookTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Function hook '{HookId}' failed", hook.Id);

            // 函数钩子错误是非阻塞的
            return HookResult.NonBlockingError(
                error: ex.Message,
                message: hook.ErrorMessage ?? $"Hook failed: {ex.Message}");
        }
    }
}

/// <summary>
/// 回调钩子执行器（内部使用）
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed partial class CallbackHookExecutor : HookExecutorBase<CallbackHook>
{
    public CallbackHookExecutor(ILogger<CallbackHookExecutor>? logger = null)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Callback;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        CallbackHook hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        LogExecutionStart(hook, input);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var context = CreateContext(hook, input);

            var result = await ExecuteWithTimeoutAsync(
                ct => hook.Callback(input, ct),
                context.Timeout,
                HookTypeConstants.Callback,
                cancellationToken).ConfigureAwait(false);

            if (result == null)
            {
                result = HookResult.Success();
            }

            LogExecutionComplete(hook, result, stopwatch.Elapsed);
            return result;
        }
        catch (HookTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Callback hook failed");

            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"Callback failed: {ex.Message}");
        }
    }
}
