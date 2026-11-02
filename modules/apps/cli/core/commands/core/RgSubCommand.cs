namespace JoinCode.CliCommands;

/// <summary>
/// ripgrep 兼容搜索子命令 — 跨进程调用 jcc-rg.exe（独立项目 modules/tools/rg/）。
/// <para>ADR: 0070 — rg 逻辑剥离为独立 exe，修改 rg 只编译 rg 项目（秒级），无需重编译 jcc。</para>
/// <para>透传参数 + stdin/stdout/stderr，返回子进程退出码。</para>
/// </summary>
internal static class RgSubCommand {
    /// <summary>跨进程调用 jcc-rg.exe 执行搜索。</summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var rgExePath = Path.Combine(AppContext.BaseDirectory, "jcc-rg.exe");
        if (!File.Exists(rgExePath)) {
            Console.Error.WriteLine($"错误: 找不到 jcc-rg.exe: {rgExePath}");
            return 1;
        }

        var psi = new ProcessStartInfo {
            FileName = rgExePath,
            UseShellExecute = false,
            RedirectStandardInput = Console.IsInputRedirected,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        for (var i = 1; i < args.Length; i++)
            psi.ArgumentList.Add(args[i]);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var tasks = new List<Task>(3);
        if (Console.IsInputRedirected)
            tasks.Add(PumpStdinAsync(process, ct));
        tasks.Add(process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(), ct));
        tasks.Add(process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), ct));

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>读取 stdin 并写入子进程的 stdin，然后关闭。</summary>
    private static async Task PumpStdinAsync(Process process, CancellationToken ct) {
        var stdin = await Console.In.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }
}
