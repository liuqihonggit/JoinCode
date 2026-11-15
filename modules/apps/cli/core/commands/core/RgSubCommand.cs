namespace JoinCode.CliCommands;

/// <summary>
/// ripgrep 兼容搜索子命令 — 跨进程调用 jcc-rg.exe（独立项目 modules/tools/rg/）。
/// <para>ADR: 0070 — rg 逻辑剥离为独立 exe，修改 rg 只编译 rg 项目（秒级），无需重编译 jcc。</para>
/// <para>透传参数 + stdin/stdout/stderr，返回子进程退出码；转发细节见 <see cref="StdinForwarder"/>。</para>
/// </summary>
internal static class RgSubCommand {
    /// <summary>跨进程调用 jcc-rg.exe 执行搜索。</summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var rgExePath = Path.Combine(AppContext.BaseDirectory, "jcc-rg.exe");
        if (!File.Exists(rgExePath)) {
            Console.Error.WriteLine($"错误: 找不到 jcc-rg.exe: {rgExePath}");
            return 1;
        }

        var psi = new ProcessStartInfo { FileName = rgExePath, UseShellExecute = false };
        for (var i = 1; i < args.Length; i++)
            psi.ArgumentList.Add(args[i]);

        return await StdinForwarder.RunAsync(psi, ct).ConfigureAwait(false);
    }
}
