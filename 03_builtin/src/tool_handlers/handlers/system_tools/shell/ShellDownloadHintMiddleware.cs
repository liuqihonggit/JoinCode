namespace Tools.Shell;

/// <summary>
/// Shell 下载提示中间件 — 检测 curl/wget 下载命令,首次提示使用 download_file 工具
/// <para>不屏蔽原功能:检测到 curl/wget 下载 → 短路返回提示(建议用 download_file 工具)</para>
/// <para>LLM 无视提示:添加 --no-intercept 参数确认,中间件去掉该参数后放行到真实执行</para>
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellDownloadHintMiddleware : ServiceEntity, IShellMiddleware
{
    private const string NoInterceptFlag = "--no-intercept";

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var command = context.Command;

        if (IsDownloadCommand(command))
        {
            if (command.Contains(NoInterceptFlag, StringComparison.Ordinal))
            {
                context.Command = StripNoInterceptFlag(command);
                await next(context, ct).ConfigureAwait(false);
                return;
            }

            var hint = BuildDownloadHint(command);
            context.Result = ToolResultBuilder.Success().WithText(hint).Build();
            context.ExecutionResult = new SystemActuatorExecutionResult
            {
                Stdout = hint,
                Stderr = string.Empty,
                ExitCode = 0,
            };
            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检测命令是否为 curl/wget 下载到文件的命令
    /// </summary>
    internal static bool IsDownloadCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var trimmed = command.TrimStart();
        if (trimmed.StartsWith('"')) return false;

        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith("curl ", StringComparison.Ordinal))
        {
            var hasOutput = lower.Contains(" -o ", StringComparison.Ordinal)
                         || lower.Contains(" --output ", StringComparison.Ordinal);
            var outputToStdout = lower.Contains(" -o -", StringComparison.Ordinal)
                              || lower.Contains(" --output -", StringComparison.Ordinal);
            return hasOutput && !outputToStdout;
        }

        if (lower.StartsWith("wget ", StringComparison.Ordinal))
        {
            var outputToStdout = lower.Contains(" -o -", StringComparison.Ordinal)
                              || lower.Contains(" -o -", StringComparison.Ordinal)
                              || lower.Contains(" --output-document=-", StringComparison.Ordinal);
            return !outputToStdout;
        }

        return false;
    }

    /// <summary>
    /// 去掉 --no-intercept 参数,清理多余空格
    /// </summary>
    internal static string StripNoInterceptFlag(string command)
    {
        var cleaned = command.Replace(NoInterceptFlag, string.Empty, StringComparison.Ordinal);
        while (cleaned.Contains("  ", StringComparison.Ordinal))
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        return cleaned.Trim();
    }

    /// <summary>
    /// 构建下载提示信息
    /// </summary>
    internal static string BuildDownloadHint(string command)
    {
        var sb = new StringBuilder();
        sb.AppendLine("检测到使用 curl/wget 下载文件。建议使用 download_file 工具(支持多线程并发 + 断点续传,更快更可靠)。");
        sb.AppendLine();
        sb.AppendLine("使用 download_file 工具:");
        sb.AppendLine("  download_file(url=\"下载地址\", file_path=\"保存路径\")");
        sb.AppendLine();
        sb.AppendLine("如需继续使用 curl/wget,请添加 --no-intercept 参数确认:");
        sb.Append("  ");
        sb.Append(command.Trim());
        sb.Append(' ');
        sb.Append(NoInterceptFlag);
        return sb.ToString();
    }
}
