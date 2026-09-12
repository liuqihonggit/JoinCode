namespace JoinCode.Cli.Commands.Prefix;

/// <summary>
/// Shell 命令执行工具 — 用 Process.Start 直接执行，捕获输出。
/// ! / !! 前缀命令共享，不经过 MCP 中间件管道（用户主动输入已授权）。
/// </summary>
internal static class ShellExecutor
{
    /// <summary>
    /// 执行 shell 命令，返回合并的 stdout+stderr 输出。
    /// </summary>
    /// <param name="command">shell 命令</param>
    /// <param name="workingDirectory">工作目录（null=当前目录）</param>
    /// <param name="timeoutMs">超时毫秒</param>
    /// <param name="maxOutputChars">输出最大字符数（截断防撑爆上下文）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<string> RunAsync(
        string command,
        string? workingDirectory,
        int timeoutMs,
        int maxOutputChars,
        CancellationToken cancellationToken)
    {
        var (shell, shellArg) = GetShellAndArg();
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"{shellArg} {command}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) errorBuilder.AppendLine(e.Data); };

        if (!process.Start())
            return "[错误] 无法启动进程。";

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception killEx) { Console.WriteLine($"[ShellExecutor] 进程终止失败: {killEx.Message}"); }
            return $"[超时] 命令在 {timeoutMs / 1000}s 内未完成，已终止。";
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        var combined = new StringBuilder();
        if (output.Length > 0)
            combined.Append(output.TrimEnd());
        if (error.Length > 0)
        {
            if (combined.Length > 0) combined.AppendLine();
            combined.Append("[stderr] ").Append(error.TrimEnd());
        }

        if (combined.Length > maxOutputChars)
        {
            combined.Length = maxOutputChars;
            combined.Append("\n[输出已截断]");
        }

        return combined.ToString();
    }

    /// <summary>
    /// 获取当前平台的 shell 及参数标志 — Windows: cmd /c，Linux/macOS: bash -c。
    /// </summary>
    private static (string Shell, string Arg) GetShellAndArg()
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", "/c");
        if (OperatingSystem.IsMacOS())
            return ("/bin/bash", "-c");
        return ("/bin/sh", "-c");
    }
}
