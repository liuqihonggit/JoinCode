namespace JoinCode.Tui;

/// <summary>
/// jcctui.exe 入口 — 独立 TUI 终端界面，不走 CLI 管道。
/// 用 EngineSessionFactory.CreateGuiSessionAsync 组装 DI（不含 PipeModule/CliModule），启动 Terminal.Gui 事件循环。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var awaitSeconds = ParseAwaitSeconds(args);
        using var awaitCts = new CancellationTokenSource();
        if (awaitSeconds is { } s && s > 0)
            awaitCts.CancelAfter(TimeSpan.FromSeconds(s));

        try
        {
            WriteDiag("[Main] CreateGuiSessionAsync start");
            // T2：注入 TUI 交互模块 — ask_user_question 走 Terminal.Gui 对话框而非 Core Mock
            var result = EngineSessionFactory.CreateGuiSessionAsync(
                extraModules: [new Hosting.TuiInteractionModule()],
                cancellationToken: awaitCts.Token).GetAwaiter().GetResult();
            WriteDiag("[Main] session created, starting TuiModeRunner");
            try
            {
                TuiModeRunner.RunAsync(result.Config, result.Services, awaitCts.Token).GetAwaiter().GetResult();
                WriteDiag("[Main] TuiModeRunner returned normally");
            }
            finally
            {
                try
                {
                    if (result.Host is IAsyncDisposable ad) ad.DisposeAsync().GetAwaiter().GetResult();
                    else result.Host.Dispose();
                }
                catch (Exception disposeEx) { WriteDiag($"[Main] Host dispose failed (ignored): {disposeEx.Message}"); }
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 1234;
        }
        catch (Exception ex)
        {
            WriteDiag($"[Main] startup failed: {ex}");
            Console.Error.WriteLine($"jcctui 启动失败: {ex.Message}");
            return 1;
        }
    }

    private static void WriteDiag(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcctui_diag");
            System.IO.Directory.CreateDirectory(dir);
            SafeFileIO.AppendAllText(
                System.IO.Path.Combine(dir, "run.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch (Exception logEx) { Console.Error.WriteLine($"[diag] WriteDiag failed: {logEx.Message}"); }
    }

    /// <summary>--await N 超时诊断参数（超时返回 1234，用于测试验收闪退）</summary>
    private static int? ParseAwaitSeconds(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--await" && int.TryParse(args[i + 1], out var s))
                return s;
        }
        return null;
    }
}
