namespace Tools.Shell;

/// <summary>
/// Shell 编译拦截中间件 — 拦截 dotnet build/test/publish/msbuild 命令
/// 同步等待模式：提交编译后等待结果，30s 超时提示 AI 自行决策
/// 缓存命中直接返回结果，同命令编译中共享结果
/// </summary>
[Register]
public sealed partial class ShellBuildInterceptMiddleware : IShellMiddleware
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Inject] private readonly IBuildQueueService _buildQueueService;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly IClockService _clock;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (IsBuildCommand(context.Command))
        {
            var agentId = _subAgentContextAccessor.Current?.AgentId ?? "main";
            var request = BuildRequest.Parse(context.Command, context.WorkingDirectory) with { AgentId = agentId };
            var buildId = await _buildQueueService.SubmitAsync(request, ct).ConfigureAwait(false);

            var entry = _buildQueueService.GetBuild(buildId);

            if (entry?.Status is BuildQueueEntryStatus.Completed or BuildQueueEntryStatus.Failed)
            {
                SetResultFromEntry(context, entry);
                return;
            }

            if (entry?.Status == BuildQueueEntryStatus.Cancelled)
            {
                context.ExecutionResult = new ShellExecutionResult
                {
                    Stdout = string.Empty,
                    Stderr = "Build was cancelled",
                    ExitCode = -1,
                    Interrupted = true,
                };
                context.Result = ResultBuilder.Error().WithText("Build was cancelled").Build();
                return;
            }

            try
            {
                var waitTask = _buildQueueService.WaitAsync(buildId, ct);
                var timeoutTask = Task.Delay(DefaultTimeout, ct);
                var completedTask = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == waitTask)
                {
                    var result = await waitTask.ConfigureAwait(false);
                    SetResultFromBuildResult(context, result);
                }
                else
                {
                    var currentEntry = _buildQueueService.GetBuild(buildId);
                    var elapsed = currentEntry?.StartedAt.HasValue == true
                        ? (int)(_clock.GetUtcNowOffset() - currentEntry.StartedAt.Value).TotalSeconds
                        : 0;
                    var statusText = currentEntry?.Status == BuildQueueEntryStatus.Queued ? "queued" : "compiling";

                    context.Result = ResultBuilder.Success()
                        .WithText($"Build {buildId} is {statusText} ({elapsed}s elapsed). Run the same build command again to wait for the result, or cancel it.")
                        .Build();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }

            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private static void SetResultFromEntry(ShellPipelineContext context, BuildQueueEntry entry)
    {
        var r = entry.Result ?? throw new InvalidOperationException("Build queue entry has no result.");
        SetResultFromBuildResult(context, r);
    }

    private static void SetResultFromBuildResult(ShellPipelineContext context, BuildQueueResult r)
    {
        var fullOutput = r.ExitCode == 0 ? r.Output : $"{r.ErrorOutput}\n{r.Output}";
        var displayOutput = TruncateOutput(fullOutput, r.BuildId);

        context.ExecutionResult = new ShellExecutionResult
        {
            Stdout = r.Output,
            Stderr = r.ErrorOutput,
            ExitCode = r.ExitCode,
        };
        context.Result = r.ExitCode == 0
            ? ResultBuilder.Success().WithText(displayOutput).Build()
            : ResultBuilder.Error().WithText(displayOutput).Build();
    }

    private const int MaxTailLines = 15;

    /// <summary>
    /// 截断编译输出 — 超过 MaxTailLines 行只返回尾部，附带行数提示
    /// </summary>
    internal static string TruncateOutput(string output, string buildId)
    {
        if (string.IsNullOrEmpty(output)) return output;

        var lines = output.Split('\n');
        if (lines.Length <= MaxTailLines) return output;

        var tail = lines[^MaxTailLines..];
        var totalLines = lines.Length;
        var startLine = totalLines - MaxTailLines + 1;

        var sb = new StringBuilder();
        sb.AppendLine($"[Output truncated: {totalLines} lines total, showing last {MaxTailLines} lines (L{startLine}-L{totalLines})]");
        sb.AppendLine($"[Use build_output build_id=\"{buildId}\" start_line=1 end_line={MaxTailLines} to read from the beginning]");
        sb.Append(string.Join('\n', tail));
        return sb.ToString();
    }

    /// <summary>
    /// 检测命令是否为 dotnet 编译类命令
    /// </summary>
    internal static bool IsBuildCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var trimmed = command.TrimStart();

        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0) return false;
            var firstToken = trimmed[1..closingQuote];
            var afterQuote = trimmed[(closingQuote + 1)..].TrimStart();
            if (string.IsNullOrEmpty(afterQuote)) return false;
            var secondSpace = afterQuote.IndexOf(' ');
            var subCommand = secondSpace >= 0 ? afterQuote[..secondSpace] : afterQuote;
            return IsDotnetBuildSubCommand(firstToken, subCommand);
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0) return false;

        var firstToken2 = trimmed[..firstSpace];
        var remaining = trimmed[(firstSpace + 1)..].TrimStart();
        var secondSpace2 = remaining.IndexOf(' ');
        var subCommand2 = secondSpace2 >= 0 ? remaining[..secondSpace2] : remaining;

        return IsDotnetBuildSubCommand(firstToken2, subCommand2);
    }

    private static bool IsDotnetBuildSubCommand(string executablePath, string subCommand)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (!executableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return false;

        return subCommand switch
        {
            "build" => true,
            "test" => true,
            "publish" => true,
            "msbuild" => true,
            _ => false
        };
    }
}
