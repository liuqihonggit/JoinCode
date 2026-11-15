namespace JoinCode.CliCommands;

/// <summary>
/// GitHub 子命令 — 跨进程调用 jcc-gh.exe（独立项目 modules/tools/gh/）。
/// <para>gh 逻辑剥离为独立 exe，修改 gh 只编译 gh 项目（秒级），无需重编译 jcc。</para>
/// <para>透传参数 + stdin/stdout/stderr，返回子进程退出码；转发细节见 <see cref="StdinForwarder"/>。</para>
/// </summary>
internal static class GhSubCommand {
    /// <summary>跨进程调用 jcc-gh.exe 执行 GitHub 操作。</summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var ghExePath = Path.Combine(AppContext.BaseDirectory, "jcc-gh.exe");
        if (!File.Exists(ghExePath)) {
            Console.Error.WriteLine($"错误: 找不到 jcc-gh.exe: {ghExePath}");
            return 1;
        }

        var psi = new ProcessStartInfo { FileName = ghExePath, UseShellExecute = false };
        for (var i = 1; i < args.Length; i++)
            psi.ArgumentList.Add(args[i]);

        return await StdinForwarder.RunAsync(psi, ct).ConfigureAwait(false);
    }
}
