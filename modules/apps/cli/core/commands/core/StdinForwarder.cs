namespace JoinCode.CliCommands;

/// <summary>
/// 启动卫星 exe 并透传标准流 —— gh / rg 两个子命令的共用执行器。
/// <para>为什么收成一处：这段逻辑原来在 <c>GhSubCommand</c> 与 <c>RgSubCommand</c> 各写一遍，
/// 于是同一个死锁缺陷也复制了两份（见下）。</para>
/// </summary>
internal static class StdinForwarder {
    /// <summary>下游提前关闭输出管道时返回的退出码，与 SIGPIPE 的 141 对齐。</summary>
    public const int BrokenPipeExitCode = 141;

    /// <summary>启动子进程、透传 stdout/stderr，返回子进程退出码。</summary>
    /// <param name="psi">子进程启动信息，至少要有 FileName 与 ArgumentList。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>子进程退出码；输出管道被下游关闭时返回 <see cref="BrokenPipeExitCode"/>。</returns>
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

        var stdout = PumpAsync(process, process.StandardOutput.BaseStream, Console.OpenStandardOutput(), ct);
        var stderr = PumpAsync(process, process.StandardError.BaseStream, Console.OpenStandardError(), ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var broken = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return broken.Any(downstreamClosed => downstreamClosed) ? BrokenPipeExitCode : process.ExitCode;
    }

    /// <summary>把子进程的一条输出流搬到本进程对应流，返回是否因下游关闭而中止。</summary>
    /// <param name="process">子进程，用于在下游关闭时终止它。</param>
    /// <param name="source">子进程输出流。</param>
    /// <param name="sink">本进程标准输出 / 标准错误流。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>因下游提前关闭管道而中止时为 true。</returns>
    /// <remarks>
    /// 这里是第三个死锁点，触发条件是 <c>jcc rg ... | head -1</c> 这种最常见不过的用法：
    /// 下游读够就退出并关闭管道，本进程写失败；而子进程还在往已经没人读的管道里写，缓冲区写满后就阻塞在写调用上，
    /// 于是 <c>WaitForExitAsync</c> 永远等不到它退出 —— 实测挂 8 秒以上、零输出。
    /// 必须在下游关闭时主动终止子进程（等价于 shell 给上游发 SIGPIPE），而不是干等。
    /// </remarks>
    private static async Task<bool> PumpAsync(Process process, Stream source, Stream sink, CancellationToken ct) {
        try {
            await source.CopyToAsync(sink, ct).ConfigureAwait(false);
            return false;
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException or OperationCanceledException) {
            Debug.WriteLine($"[StdinForwarder] 输出转发中止（{ex.GetType().Name}），按 SIGPIPE 语义终止子进程");
            Terminate(process);
            return true;
        }
    }

    /// <summary>终止子进程；进程已退出或无法终止时静默跳过。</summary>
    private static void Terminate(Process process) {
        try {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        } catch (InvalidOperationException) {
            Debug.WriteLine("[StdinForwarder] 子进程已退出，无需终止");
        } catch (NotSupportedException) {
            Debug.WriteLine("[StdinForwarder] 当前平台不支持终止子进程树");
        } catch (System.ComponentModel.Win32Exception ex) {
            // 权限不足或进程刚被系统回收：此时等待退出即可，不必让转发失败升级成未处理异常。
            Debug.WriteLine($"[StdinForwarder] 终止子进程失败: {ex.NativeErrorCode}");
        }
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
