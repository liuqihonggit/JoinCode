
namespace Core.Hooks.Execution;

/// <summary>
/// Bash 命令钩子执行器
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed class BashCommandHookExecutor : HookExecutorBase<BashCommandHook>
{
    private const string DefaultShell = "bash";

    private readonly IProcessService _processService;

    public BashCommandHookExecutor(IProcessService processService, ILogger<BashCommandHookExecutor>? logger = null)
        : base(logger)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Command;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        BashCommandHook hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        LogExecutionStart(hook, input);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var context = CreateContext(hook, input);
            var inputJson = PrepareInputJson(input);
            var processedCommand = SubstituteArguments(hook.Command, inputJson);

            // 异步钩子只启动进程不等待
            if (hook.Async == true || hook.AsyncRewake == true)
            {
                _ = ExecuteAsyncInternal(hook, processedCommand, input, cancellationToken);
                return HookResult.Success();
            }

            var result = await ExecuteWithTimeoutAsync(
                ct => ExecuteCommandAsync(hook, processedCommand, input, ct),
                context.Timeout,
                hook.GetDisplayText(),
                cancellationToken).ConfigureAwait(false);

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
            Logger?.LogError(ex, "Failed to execute bash command hook");
            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"Hook execution failed: {ex.Message}");
        }
    }

    private async Task<HookResult> ExecuteAsyncInternal(
        BashCommandHook hook,
        string command,
        HookInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteCommandAsync(hook, command, input, cancellationToken).ConfigureAwait(false);

            // 异步唤醒：退出码 2 时触发唤醒
            if (hook.AsyncRewake == true &&
                result.BlockingError != null)
            {
                if (input.OnModelWake != null)
                {
                    try
                    {
                        await input.OnModelWake(input.EventName).ConfigureAwait(false);
                        Logger?.LogInformation(
                            "Async hook triggered model wake for event {Event}",
                            input.Event);
                    }
                    catch (Exception wakeEx)
                    {
                        Logger?.LogError(wakeEx, "Model wake callback failed for event {Event}", input.Event);
                    }
                }
                else
                {
                    Logger?.LogWarning(
                        "Async hook requested model wake but no wake callback configured for event {Event}",
                        input.Event);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Async hook execution failed");
            return HookResult.NonBlockingError(error: ex.Message);
        }
    }

    private async Task<HookResult> ExecuteCommandAsync(
        BashCommandHook hook,
        string command,
        HookInput input,
        CancellationToken cancellationToken)
    {
        var shell = hook.Shell ?? DefaultShell;
        var (fileName, arguments) = GetShellCommand(shell, command);

        var envVars = new Dictionary<string, string>
        {
            ["HOOK_EVENT"] = input.Event.ToEventName(),
            ["HOOK_TOOL_NAME"] = input.ToolName ?? "",
            ["HOOK_TOOL_USE_ID"] = input.ToolUseId ?? "",
            ["HOOK_SESSION_ID"] = input.SessionId ?? ""
        };

        try
        {
            var options = new ProcessOptions
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Environment.CurrentDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                EnvironmentVariables = envVars
            };

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            return ParseHookOutput(
                result.StandardOutput,
                result.StandardError,
                result.ExitCode,
                command,
                isAsync: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static (string FileName, string Arguments) GetShellCommand(string shell, string command)
    {
        var shellType = ShellTypeHelper.ParseShellType(shell) ?? ShellType.Bash;
        return shellType switch
        {
            ShellType.PowerShell => ("pwsh", $"-Command \"{command.Replace("\"", "\"\"")}\""),
            ShellType.Cmd => ("cmd.exe", $"/c \"{command.Replace("\"", "\"\"")}\""),
            _ => ("bash", $"-c \"{command.Replace("\"", "\\\"")}\"")
        };
    }
}
