namespace Tools.Shell;

/// <summary>
/// Shell 输出格式化中间件 — 处理执行结果的输出格式化
/// 包括中断检测、图片输出检测、输出构建、命令语义解释
/// </summary>
[Register]
public sealed partial class ShellOutputMiddleware : IShellMiddleware
{
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(ShellContext context, MiddlewareDelegate<ShellContext> next, CancellationToken ct)
    {
        var result = context.ExecutionResult;
        if (result is null)
        {
            context.Result = ResultBuilder.Error().WithText("No execution result available").Build();
            return Task.CompletedTask;
        }

        var shellType = context.IsPowerShell ? "powershell" : "cmd";

        if (result.Interrupted)
        {
            RecordShellMetrics(shellType, "execute", "interrupted");
            // 对齐 TS: interrupted 时 is_error=true，输出包含 <error> 标签
            context.Result = ResultBuilder.Error().WithText(BuildOutputResponse(result, context.Command)).Build();
            return Task.CompletedTask;
        }

        // 对齐 TS BashTool: 检测 stdout 中的 Data URI 图片输出
        if (ShellImageOutputDetector.IsImageOutput(result.Stdout))
        {
            var parsed = ShellImageOutputDetector.ParseDataUri(result.Stdout);
            if (parsed is { } img)
            {
                var (resizedMediaType, resizedBase64Data) = ShellImageOutputDetector.ResizeIfOversized(img.MediaType, img.Base64Data) ?? img;
                RecordShellMetrics(shellType, "execute", "ok");
                context.Result = new ToolResult
                {
                    Content = [new ToolContent { Type = ToolContentType.Image, Data = resizedBase64Data, MimeType = resizedMediaType }],
                    IsImage = true,
                };
                return Task.CompletedTask;
            }
        }

        var output = BuildOutputResponse(result, context.Command);

        // 对齐 TS claudeCodeHints: 扫描并剥离 <claude-code-hint /> 标签
        var hintResult = ClaudeCodeHintExtractor.Extract(output, context.Command);
        if (hintResult.Hints.Count > 0)
        {
            _telemetryService?.RecordCount("shell.hints.detected", new Dictionary<string, string> { ["type"] = string.Join(",", hintResult.Hints.Select(h => h.Type)) }, description: "Claude Code hints detected");
        }
        output = hintResult.StrippedOutput;

        // 对齐 TS commandSemantics: 使用语义判断是否为错误（如 grep exit 1 不是错误）
        var interpretation = InterpretCommandResult(context.Command, result.ExitCode ?? 0, result.Stdout ?? string.Empty, result.Stderr ?? string.Empty);
        if (interpretation.IsError)
        {
            RecordShellMetrics(shellType, "execute", "failed");
            context.Result = ResultBuilder.Error().WithText(output).Build();
            return Task.CompletedTask;
        }

        RecordShellMetrics(shellType, "execute", "ok");
        context.Result = ResultBuilder.Success().WithText(output).Build();
        return Task.CompletedTask;
    }

    private void RecordShellMetrics(string shellType, string operation, string result)
        => _telemetryService?.RecordCount("shell.execution.count", new Dictionary<string, string> { ["shell"] = shellType, ["operation"] = operation, ["result"] = result }, description: "Shell execution count");

    /// <summary>
    /// 构建输出响应 — 对齐 TS mapToolResultToToolResultBlockParam
    /// </summary>
    private static string BuildOutputResponse(ShellExecutionResult result, string? command = null)
    {
        // 对齐 TS mapToolResultToToolResultBlockParam: stdout 处理
        var processedStdout = result.Stdout ?? string.Empty;
        if (processedStdout.Length > 0)
        {
            // 去除前导空行 — 对齐 TS stdout.replace(/^(\s*\n)+/, '')
            processedStdout = LeadingBlankLineRegex().Replace(processedStdout, string.Empty);
            processedStdout = processedStdout.TrimEnd();
        }

        // 大输出持久化 — 对齐 TS persistedOutputPath
        if (result.PersistedOutputPath is not null)
        {
            processedStdout = result.BuildPersistedOutputMessage();
        }

        // stderr + interrupted 处理 — 对齐 TS errorMessage
        var errorMessage = (result.Stderr ?? string.Empty).Trim();
        if (result.Interrupted)
        {
            if (errorMessage.Length > 0) errorMessage += Environment.NewLine;
            errorMessage += "<error>Command was aborted before completion</error>";
        }

        // 后台任务信息 — 对齐 TS backgroundInfo
        var backgroundInfo = BuildBackgroundInfo(result);

        // 退出码语义解释 — 对齐 TS returnCodeInterpretation
        var interpretation = InterpretCommandResult(command, result.ExitCode ?? 0, result.Stdout ?? string.Empty, result.Stderr ?? string.Empty);
        var interpretationInfo = interpretation.Message is not null
            ? $"[Exit code: {result.ExitCode} — {interpretation.Message}]"
            : result.ExitCode.HasValue && result.ExitCode.Value != 0
                ? $"[Exit code: {result.ExitCode}]"
                : null;

        // 拼接最终输出 — 对齐 TS [processedStdout, errorMessage, backgroundInfo].filter(Boolean).join('\n')
        var parts = new List<string>(4);
        if (processedStdout.Length > 0) parts.Add(processedStdout);
        if (errorMessage.Length > 0) parts.Add(errorMessage);
        if (backgroundInfo.Length > 0) parts.Add(backgroundInfo);
        if (interpretationInfo is not null) parts.Add(interpretationInfo);

        var output = string.Join(Environment.NewLine, parts);
        return string.IsNullOrEmpty(output) ? "(No output)" : output;
    }

    /// <summary>
    /// 构建后台任务信息 — 对齐 TS mapToolResultToToolResultBlockParam backgroundInfo
    /// </summary>
    private static string BuildBackgroundInfo(ShellExecutionResult result)
    {
        if (result.BackgroundTaskId is null) return string.Empty;

        // 对齐 TS: 区分 assistantAutoBackgrounded / backgroundedByUser / 默认
        if (result.AssistantAutoBackgrounded)
        {
            return $"Command exceeded the assistant-mode blocking budget and was moved to the background with ID: {result.BackgroundTaskId}. It is still running — you will be notified when it completes.";
        }

        if (result.BackgroundedByUser)
        {
            return $"Command was manually backgrounded by user with ID: {result.BackgroundTaskId}.";
        }

        return $"Command running in background with ID: {result.BackgroundTaskId}.";
    }

    /// <summary>
    /// 退出码语义解释 — 对齐 TS commandSemantics.ts interpretCommandResult
    /// 返回 (IsError, Message) 元组，某些命令的非零退出码不代表错误
    /// </summary>
    private static (bool IsError, string? Message) InterpretCommandResult(string? command, int exitCode, string stdout, string stderr)
    {
        if (string.IsNullOrEmpty(command)) return (exitCode != 0, exitCode != 0 ? $"Command failed with exit code {exitCode}" : null);
        var baseCmd = GetBaseCommand(command);
        return baseCmd switch
        {
            // grep/rg: 0=匹配, 1=无匹配(非错误), 2+=错误 — 对齐 TS COMMAND_SEMANTICS
            "grep" or "rg" or "ack" or "ag" or "findstr" => exitCode switch
            {
                1 => (false, "No matches found"),
                >= 2 => (true, null),
                _ => (false, null)
            },
            // find: 0=成功, 1=部分成功, 2+=错误
            "find" => exitCode switch
            {
                1 => (false, "Some directories were inaccessible"),
                >= 2 => (true, null),
                _ => (false, null)
            },
            // diff: 0=无差异, 1=有差异(非错误), 2+=错误
            "diff" => exitCode switch
            {
                1 => (false, "Files differ"),
                >= 2 => (true, null),
                _ => (false, null)
            },
            // test/[: 0=条件真, 1=条件假(非错误), 2+=错误
            "test" or "[" => exitCode switch
            {
                1 => (false, "Condition is false"),
                >= 2 => (true, null),
                _ => (false, null)
            },
            // 默认语义 — 对齐 TS DEFAULT_SEMANTIC
            _ => (exitCode != 0, exitCode != 0 ? $"Command failed with exit code {exitCode}" : null)
        };
    }

    /// <summary>
    /// 提取命令的基础命令名（去除路径和参数）
    /// </summary>
    private static string GetBaseCommand(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var firstToken = spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
        return Path.GetFileNameWithoutExtension(firstToken).ToLowerInvariant();
    }

    [GeneratedRegex(@"^(\s*\n)+")]
    private static partial Regex LeadingBlankLineRegex();
}
