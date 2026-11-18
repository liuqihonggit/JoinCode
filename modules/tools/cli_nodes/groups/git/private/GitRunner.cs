

namespace JoinCode.CliNode.Git;

/// <summary>执行 git 子进程 — 只依赖 BCL，不引用引擎 DI / IFileSystem / 中间件管道。</summary>
internal static class GitRunner {
    public static async Task<int> RunAsync(string arguments, string? workingDirectory, CancellationToken ct) {
        var psi = new ProcessStartInfo("git", arguments) {
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 git 进程，请确认 git 已安装并在 PATH 中。");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        Console.Out.Write(await stdout.ConfigureAwait(false));
        Console.Error.Write(await stderr.ConfigureAwait(false));
        return process.ExitCode;
    }
}
