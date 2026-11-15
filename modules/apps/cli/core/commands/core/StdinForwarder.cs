namespace JoinCode.CliCommands;

/// <summary>
/// 启动卫星 exe 并透传标准流 —— gh / rg 两个子命令的共用执行器。
/// <para>为什么收成一处：这段逻辑原来在 <c>GhSubCommand</c> 与 <c>RgSubCommand</c> 各写一遍，
/// 于是同一个死锁缺陷也复制了两份（见下）。</para>
/// </summary>
internal static class StdinForwarder {
    /// <summary>启动子进程、透传 stdout/stderr，返回子进程退出码。</summary>
    /// <param name="psi">子进程启动信息，至少要有 FileName 与 ArgumentList。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>子进程退出码。</returns>
    public static async Task<int> RunAsync(ProcessStartInfo psi, CancellationToken ct) {
        psi.UseShellExecute = false;
        // 只有调用方 stdin 被重定向时才接管道：接管的目的是把管道里的内容喂给子进程。
        // stdin 是终端时让子进程直接继承控制台句柄 —— 交互式命令（gh auth login）才能正常读键。
        psi.RedirectStandardInput = Console.IsInputRedirected;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (psi.RedirectStandardInput) StartStdinPump(process);

        var tasks = new List<Task>(2) {
            process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(), ct),
            process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), ct)
        };

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        } catch (IOException) {
            // 子进程提前关管道：输出已尽力透传，以子进程退出码为准。
            Debug.WriteLine("[StdinForwarder] 子进程管道提前关闭，输出可能被截断");
        }
        return process.ExitCode;
    }

    /// <summary>
    /// 在长驻线程上把本进程 stdin 转发给子进程，<b>不 await</b>。
    /// <para>这里踩过两次坑，都表现为「jcc gh / jcc rg 在 CI、包装脚本、agent 环境里一声不吭地挂住」：</para>
    /// <para>① 先 <c>ReadToEndAsync</c> 再喂子进程：要等调用方 stdin 的 EOF 才返回。stdin 若是
    /// 「打开但未关闭」的管道，EOF 永远不来，双方互等成死锁。</para>
    /// <para>② 改成 <c>CopyToAsync</c> 并在子进程退出后 <c>Cancel</c> 仍然不够：Windows 上重定向的
    /// stdin 是同步原生 ReadFile，取消令牌打断不了阻塞中的读。于是子进程早已退出、输出也打印完了，
    /// 父进程还挂在等待转发任务上 —— 实测 15 秒不返回。</para>
    /// <para>结论：转发必须放在独立长驻线程、且不能进 await 链。线程随进程退出被回收即可。</para>
    /// </summary>
    private static void StartStdinPump(Process process) {
        _ = Task.Factory.StartNew(
            () => PumpStdin(process),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>逐块搬运 stdin 到子进程，EOF 或子进程关闭管道后结束。</summary>
    private static void PumpStdin(Process process) {
        var buffer = new byte[8192];
        try {
            using var stdin = Console.OpenStandardInput();
            var sink = process.StandardInput.BaseStream;
            int read;
            while ((read = stdin.Read(buffer, 0, buffer.Length)) > 0) {
                sink.Write(buffer, 0, read);
                sink.Flush();
            }
        } catch (IOException) {
            // 子进程不读 stdin 就退出了（gh auth status 这类），不算错误。
            Debug.WriteLine("[StdinForwarder] 子进程 stdin 管道已关闭，停止转发");
        } catch (ObjectDisposedException) {
            Debug.WriteLine("[StdinForwarder] 子进程 stdin 已释放，停止转发");
        } catch (InvalidOperationException) {
            Debug.WriteLine("[StdinForwarder] 子进程 stdin 不可写，停止转发");
        } finally {
            try {
                process.StandardInput.Close();
            } catch (IOException) {
                // 已是关闭状态。
                Debug.WriteLine("[StdinForwarder] 子进程 stdin 已处于关闭状态");
            } catch (ObjectDisposedException) {
                Debug.WriteLine("[StdinForwarder] 子进程 stdin 已释放");
            }
        }
    }
}
