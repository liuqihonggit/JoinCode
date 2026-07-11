using System.Diagnostics;

namespace MockServer.E2E.Tests;

/// <summary>
/// BridgeMainCommand Guard 集成 E2E 测试 — P0-C
/// 启动真实 jcc.exe remote-control 子命令，验证 Guard 检查链路：
/// 1. Bridge 功能开关 (JCC_BRIDGE_MODE)
/// 2. 策略检查 (fail-open 当无 IRemotePolicyService 注入)
/// 3. 访问令牌获取 (env / OAuth 兜底)
/// 4. --help 早期返回
/// 决策: 仅测试早期返回路径，不测试 BridgeMain.RunAsync（需真实 API）
/// </summary>
[Trait("Category", "Integration")]
public sealed class BridgeGuardIntegrationE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    public BridgeGuardIntegrationE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// 未设置 JCC_BRIDGE_MODE 环境变量时，应输出 "Bridge 功能未启用" 并以 exit 1 退出
    /// </summary>
    [Fact]
    public async Task Bridge_WhenBridgeModeNotSet_ShouldExit1WithFeatureDisabledMessage()
    {
        var (exitCode, stdout) = await RunJccRemoteControlAsync(
            envVars: new Dictionary<string, string?> { ["JCC_BRIDGE_MODE"] = null },
            args: [],
            timeoutSeconds: 15).ConfigureAwait(true);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Bridge 功能未启用");
        _output.WriteLine($"Exit: {exitCode}, Output: {stdout}");
    }

    /// <summary>
    /// JCC_BRIDGE_MODE=1 但无任何 Token 来源（env + OAuth 存储均空）时，
    /// 应输出 "无法初始化 Bridge 依赖" 并以 exit 1 退出
    /// </summary>
    [Fact]
    public async Task Bridge_WhenNoAccessToken_ShouldExit1WithNoDepsMessage()
    {
        var (exitCode, stdout) = await RunJccRemoteControlAsync(
            envVars: new Dictionary<string, string?>
            {
                ["JCC_BRIDGE_MODE"] = "1",
                ["JCC_API_KEY"] = null,
                ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
                ["CLAUDE_CODE_SESSION_ACCESS_TOKEN"] = null,
            },
            args: [],
            timeoutSeconds: 15).ConfigureAwait(true);

        exitCode.Should().Be(1);
        stdout.Should().Contain("无法初始化 Bridge 依赖");
        _output.WriteLine($"Exit: {exitCode}, Output: {stdout}");
    }

    /// <summary>
    /// JCC_BRIDGE_MODE=1 + JCC_API_KEY 设置时，
    /// Guard 检查应全部通过（fail-open 策略 + env token），
    /// 进入 BridgeMain.RunAsync 后会因 API 不可达失败，但不应在 Guard 阶段失败
    /// </summary>
    [Fact]
    public async Task Bridge_WithApiToken_ShouldPassGuardChecks_AndEnterBridgeMain()
    {
        var (exitCode, stdout) = await RunJccRemoteControlAsync(
            envVars: new Dictionary<string, string?>
            {
                ["JCC_BRIDGE_MODE"] = "1",
                ["JCC_API_KEY"] = "test-token-xxx",
                ["JCC_API_BASE_URL"] = "http://localhost:1", // 不可达端口，加速失败
            },
            args: [],
            timeoutSeconds: 35).ConfigureAwait(true);

        // Guard 检查不应失败 — 不应出现这些消息
        stdout.Should().NotContain("Bridge 功能未启用");
        stdout.Should().NotContain("远程控制已被组织策略禁用");
        stdout.Should().NotContain("无法初始化 Bridge 依赖");

        // BridgeMain 会因 API 不可达失败，exit code != 0
        // 但失败应在 BridgeMain 阶段，而非 Guard 阶段
        _output.WriteLine($"Exit: {exitCode}, Output: {stdout}");
    }

    /// <summary>
    /// --help 应输出帮助文本并以 exit 0 退出（在 Guard 检查之前）
    /// </summary>
    [Fact]
    public async Task Bridge_WithHelpFlag_ShouldExit0WithHelpText()
    {
        var (exitCode, stdout) = await RunJccRemoteControlAsync(
            envVars: new Dictionary<string, string?> { ["JCC_BRIDGE_MODE"] = null },
            args: ["--help"],
            timeoutSeconds: 15).ConfigureAwait(true);

        exitCode.Should().Be(0);
        stdout.Should().NotBeNullOrEmpty();
        _output.WriteLine($"Exit: {exitCode}, Output (first 200): {stdout[..Math.Min(200, stdout.Length)]}");
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private async Task<(int ExitCode, string Stdout)> RunJccRemoteControlAsync(
        Dictionary<string, string?> envVars,
        string[] args,
        int timeoutSeconds)
    {
        var exePath = ResolveJccExePath();
        _output.WriteLine($"[BridgeE2E] jcc.exe 路径: {exePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };

        // 子命令 remote-control
        startInfo.ArgumentList.Add("remote-control");
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // 清理可能干扰的 env vars，然后设置测试需要的
        foreach (var (key, value) in envVars)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(timeoutSeconds * 1000);
        if (!exited)
        {
            try { process.Kill(true); }
            catch (Exception ex) { _output.WriteLine($"[BridgeE2E] Kill failed: {ex.Message}"); }
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw new TimeoutException($"jcc.exe 未在 {timeoutSeconds}s 内退出");
        }

        // 确保异步读取完成
        await process.WaitForExitAsync().ConfigureAwait(true);
        var stdout = stdoutBuilder.ToString();
        var stderr = stderrBuilder.ToString();
        _output.WriteLine($"[BridgeE2E] stderr: {stderr}");

        return (process.ExitCode, stdout);
    }

    private static string ResolveJccExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir)
            ?? throw new FileNotFoundException($"未找到 artifacts/bin 目录 (从 {baseDir})");

        var found = SearchExeUnderDir(artifactsBin, "jcc.exe");
        if (found is not null) return found;

#pragma warning disable JCC9001
        // E2E 测试基础设施：解析 jcc.exe 路径，不通过 IFileSystem DI（此为构建期路径解析）
        var fallback = Path.GetFullPath(Path.Combine(baseDir, "jcc.exe"));
        if (File.Exists(fallback)) return fallback;
#pragma warning restore JCC9001

        throw new FileNotFoundException($"未找到 jcc.exe (artifacts/bin={artifactsBin})");
    }

    private static string? FindArtifactsBinRoot(string baseDir)
    {
        var dir = baseDir;
        for (var i = 0; i < 10; i++)
        {
#pragma warning disable JCC9001
            // E2E 测试基础设施：查找 artifacts/bin 目录
            var candidate = Path.Combine(dir, "artifacts", "bin");
            if (Directory.Exists(candidate)) return candidate;
#pragma warning restore JCC9001

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string? SearchExeUnderDir(string rootDir, string exeName)
    {
        try
        {
#pragma warning disable JCC9001
            // E2E 测试基础设施：搜索 exe 文件
            return Directory.GetFiles(rootDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
#pragma warning restore JCC9001
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[BridgeE2E] SearchExeUnderDir 失败: {ex.Message}");
            return null;
        }
    }
}
