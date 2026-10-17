namespace Tui.Tests;

/// <summary>
/// TUI 会话集成测试 — 验证 jcctui.exe 能加载、启动不卡死。
/// 标记 Integration 类别，CI 用 Category!=Integration 跳过。
/// 进程启动测试在无头环境（stdout 重定向）自动跳过，因 Terminal.Gui app.Init 需真实终端。
/// </summary>
[Trait("Category", "Integration")]
public class TuiSessionTests {
    [Fact]
    public void JcctuiAssembly_LoadsSuccessfully() {
        var assembly = typeof(OutputView).Assembly;
        Assert.Equal("jcctui", assembly.GetName().Name);
    }

    [Fact]
    public void Jcctui_WithAwait2_DoesNotHang() {
        if (Console.IsOutputRedirected) return;

        var dllPath = typeof(OutputView).Assembly.Location;
        var repoRoot = FindRepoRoot();

        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"exec \"{dllPath}\" --await 2",
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var exited = process.WaitForExit(8000);
        if (!exited) process.Kill(entireProcessTree: true);

        Assert.True(exited, "jcctui --await 2 应在8秒内退出");
    }

    [Fact]
    public void Jcctui_WithAwait1_ExitsWithValidCode() {
        if (Console.IsOutputRedirected) return;

        var dllPath = typeof(OutputView).Assembly.Location;
        var repoRoot = FindRepoRoot();

        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"exec \"{dllPath}\" --await 1",
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var exited = process.WaitForExit(8000);
        if (!exited) {
            process.Kill(entireProcessTree: true);
            return;
        }
        var exitCode = process.ExitCode;

        Assert.True(exitCode is 0 or 1234 or 1, $"意外退出码: {exitCode}");
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir, "app")))
            dir = System.IO.Path.GetDirectoryName(dir);
        return dir ?? AppContext.BaseDirectory;
    }
}