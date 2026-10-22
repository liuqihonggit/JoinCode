namespace NativeTui.Core.Repl;

/// <summary>bash 直通执行结果 — stdout/stderr/退出码/中断标记。</summary>
/// <param name="StdOut">标准输出全文（UTF-8 解码）。</param>
/// <param name="StdErr">标准错误全文（UTF-8 解码；进程启动失败时为失败原因消息）。</param>
/// <param name="ExitCode">退出码（中断/启动失败为 -1）。</param>
/// <param name="Interrupted">用户取消中断（对齐基准 ShellError.interrupted 分支语义）。</param>
public sealed record BashDirectResult(string StdOut, string StdErr, int ExitCode, bool Interrupted);

/// <summary>
/// bash 直通执行器 — 对齐基准 processBashCommand.tsx 的 `!` 直通分支（基准 inputModes.ts:16-21
/// 的 `!` 前缀 → bash 模式）。纯执行器无 UI：`!` 前缀检测/边框变色/BashModeProgress 属
/// ReplShell/PromptInputBox 域（接线未决问题）。进程封装纯净形态（BridgeClient 长驻 stdio
/// 管道先例之外的一次性命令执行）：using var 进程 + ArgumentList 免拼接转义 + UTF-8 输出解码 +
/// 双流并发读（防管道缓冲填满死锁）+ CancellationToken 取消守卫（取消 → Kill 进程树 +
/// 无超时等收尾，返回已收集输出 + Interrupted 标记，不抛）；启动失败降级 StdErr 失败消息
///（对齐基准 catch errorMessage 塞 stderr 语义，不抛给调用方）。
/// </summary>
public sealed class BashDirectExecutor
{
    /// <summary>默认 shell（基准 bash 直通；Windows 宿主接线可注入 cmd.exe / pwsh）。</summary>
    public const string DefaultShellFileName = "bash";

    /// <summary>默认命令参数开关（bash -c；cmd.exe 用 /c）。</summary>
    public const string DefaultCommandFlag = "-c";

    private readonly string _shellFileName;
    private readonly string _commandFlag;

    /// <summary>创建执行器 — shellFileName 为 shell 可执行名/路径；commandFlag 为整命令参数开关（bash -c / cmd.exe /c）。</summary>
    public BashDirectExecutor(string shellFileName = DefaultShellFileName, string commandFlag = DefaultCommandFlag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandFlag);
        _shellFileName = shellFileName;
        _commandFlag = commandFlag;
    }

    /// <summary>执行命令并收集输出 — 取消时杀进程树返回部分输出 + Interrupted；
    /// 启动失败（shell 不存在/权限拒绝）返回 StdErr 失败消息 + 退出码 -1，均不抛异常。</summary>
    public async Task<BashDirectResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        try
        {
            using var process = StartProcess(command);
            return await CollectAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException)
        {
            return new BashDirectResult(
                string.Empty,
                $"命令执行失败：无法启动 shell「{_shellFileName}」（{ex.Message}）",
                -1,
                false);
        }
    }

    /// <summary>启动 shell 进程 — ArgumentList 免拼接转义；重定向双流 + UTF-8 解码 + 无窗口。</summary>
    private Process StartProcess(string command)
    {
        var psi = new ProcessStartInfo(_shellFileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(_commandFlag);
        psi.ArgumentList.Add(command);
        return Process.Start(psi) ?? throw new InvalidOperationException($"shell 进程启动失败：{_shellFileName}");
    }

    /// <summary>收集输出 — 双流并发读（防管道缓冲填满死锁）；取消 → Kill 进程树 + 等收尾
    ///（无超时等待 — Kill 后管道关闭、读取任务自然完成，返回已缓冲部分输出）。</summary>
    private static async Task<BashDirectResult> CollectAsync(Process process, CancellationToken cancellationToken)
    {
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var interrupted = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            interrupted = true;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new BashDirectResult(stdout, stderr, interrupted ? -1 : process.ExitCode, interrupted);
    }

    /// <summary>杀进程树 — 进程已退出/关联失败时静默（无操作或吞异常，取消路径尽力而为）。</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
