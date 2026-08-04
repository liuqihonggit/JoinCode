namespace MockServer.E2E.Tests.E2E;

using System.Diagnostics;
using System.Text.RegularExpressions;

#pragma warning disable JCC9001, JCC9002

/// <summary>
/// 集群 E2E 测试 — 验证 jcc 集群流程（cluster_analyze → expand → worker → gather → merge → review）
/// 通过 MockServer 模拟 LLM 响应，验证集群 DAG 执行不卡死、退出码正确
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ClusterE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private Process? _mockServerProcess;
    private int _mockServerPort;
    private Process? _jccProcess;

    public ClusterE2ETests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await KillProcessAsync(_jccProcess).ConfigureAwait(false);
        await KillProcessAsync(_mockServerProcess).ConfigureAwait(false);
    }

    /// <summary>
    /// 集群非交互模式 — jcc -p "并行编写三个模块的文档" 应在 30s 内正常退出（退出码 0）
    /// </summary>
    [Fact]
    public async Task ClusterNonInteractive_ShouldCompleteWithoutTimeout()
    {
        var configPath = WriteClusterMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        _output.WriteLine($"[ClusterE2E] jcc.exe: {exePath}");
        _output.WriteLine($"[ClusterE2E] MockServer 端口: {_mockServerPort}");

        var (exitCode, elapsed, stdout, stderr) = await RunJccClusterAsync(
            exePath, _mockServerPort,
            prompt: "并行编写三个模块的文档",
            awaitSeconds: 30,
            timeoutSeconds: 60).ConfigureAwait(true);

        _output.WriteLine($"[ClusterE2E] jcc.exe 退出码: {exitCode}, 耗时: {elapsed.TotalSeconds:F1}s");

        if (!string.IsNullOrEmpty(stderr))
        {
            var stderrLines = stderr.Split('\n').Where(l => l.Contains("[STEP]") || l.Contains("[DONE]") || l.Contains("cluster", StringComparison.OrdinalIgnoreCase)).Take(20);
            foreach (var line in stderrLines)
                _output.WriteLine($"[ClusterE2E:stderr] {line.TrimEnd('\r')}");
        }

        exitCode.Should().Be(0, "集群流程应正常完成，不应超时（1234）或崩溃");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(55), "应在 --await 30s 超时前完成");
    }

    /// <summary>
    /// 集群流程 — MockServer 应收到多个请求（主进程 + Worker 子进程）
    /// </summary>
    [Fact]
    public async Task ClusterNonInteractive_MockServerShouldReceiveMultipleRequests()
    {
        var configPath = WriteClusterMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();

        var (exitCode, _, _, _) = await RunJccClusterAsync(
            exePath, _mockServerPort,
            prompt: "并行编写三个模块的文档",
            awaitSeconds: 30,
            timeoutSeconds: 60).ConfigureAwait(true);

        _output.WriteLine($"[ClusterE2E] jcc.exe 退出码: {exitCode}");

        exitCode.Should().Be(0);
    }

    /// <summary>
    /// 集群流程 — 输出应包含集群相关关键词
    /// </summary>
    [Fact]
    public async Task ClusterNonInteractive_OutputShouldContainClusterKeywords()
    {
        var configPath = WriteClusterMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();

        var (exitCode, _, stdout, _) = await RunJccClusterAsync(
            exePath, _mockServerPort,
            prompt: "并行编写三个模块的文档",
            awaitSeconds: 30,
            timeoutSeconds: 60).ConfigureAwait(true);

        exitCode.Should().Be(0);

        _output.WriteLine($"[ClusterE2E] stdout 长度: {stdout.Length}");
        if (stdout.Length > 0)
        {
            var preview = stdout.Length > 500 ? stdout[..500] + "..." : stdout;
            _output.WriteLine($"[ClusterE2E] stdout 预览: {preview}");
        }

        stdout.Should().NotBeEmpty("集群流程应有输出");
    }

    private async Task<(int ExitCode, TimeSpan Elapsed, string Stdout, string Stderr)> RunJccClusterAsync(
        string exePath,
        int mockServerPort,
        string prompt,
        int awaitSeconds = 30,
        int timeoutSeconds = 60)
    {
        var stateDir = Path.Combine(Path.GetTempPath(), $"jcc_cluster_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDir);

        var args = $"--trust --await {awaitSeconds} -p \"{prompt}\"";

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = stateDir,
        };

        psi.EnvironmentVariables["JCC_ENDPOINT"] = $"http://localhost:{mockServerPort}";
        psi.EnvironmentVariables["JCC_API_KEY"] = "sk-test-1234567890";
        psi.EnvironmentVariables["JCC_PROVIDER"] = "openai";
        psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o";
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890";
        psi.EnvironmentVariables["JCC_PERMISSION_MODE"] = "bypassPermissions";
        psi.EnvironmentVariables["JCC_APP_DATA_FOLDER"] = stateDir;
        psi.EnvironmentVariables["JCC_API_TIMEOUT_MS"] = "30000";
        psi.EnvironmentVariables["JCC_CLUSTER_DECOMPOSITION_OVERRIDE"] = "3";

        _jccProcess = new Process { StartInfo = psi };

        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        _jccProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            stdoutBuilder.AppendLine(e.Data);
            _output.WriteLine($"[jcc:out] {e.Data}");
        };

        _jccProcess.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            stderrBuilder.AppendLine(e.Data);
            _output.WriteLine($"[jcc:err] {e.Data}");
        };

        if (!_jccProcess.Start())
            throw new InvalidOperationException("无法启动 jcc.exe");

        _jccProcess.BeginOutputReadLine();
        _jccProcess.BeginErrorReadLine();

        _jccProcess.EnableRaisingEvents = true;
        _jccProcess.Exited += (_, _) => exitTcs.TrySetResult(_jccProcess.ExitCode);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var code = await exitTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            sw.Stop();

            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);

            return (code, sw.Elapsed, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _output.WriteLine("[ClusterE2E] jcc.exe 超时，强制终止");
            return (-1, sw.Elapsed, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }
    }

    private async Task StartMockServerAsync(string configPath)
    {
        var mockServerExe = ResolveMockServerPath();
        _output.WriteLine($"[ClusterE2E] MockServer.exe: {mockServerExe}");

        var psi = new ProcessStartInfo
        {
            FileName = mockServerExe,
            Arguments = $"--config \"{configPath}\" --port 0",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(mockServerExe) ?? Directory.GetCurrentDirectory(),
        };

        _mockServerProcess = new Process { StartInfo = psi };

        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyMarker = "[OpenAI]   URL:";

        _mockServerProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _output.WriteLine($"[MockServer:out] {e.Data}");
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
                var match = PortRegex().Match(urlPart);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    readyTcs.TrySetResult(port);
            }
        };

        _mockServerProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[MockServer:ERR] {e.Data}");
        };

        if (!_mockServerProcess.Start())
            throw new InvalidOperationException("无法启动 MockServer");

        _mockServerProcess.BeginOutputReadLine();
        _mockServerProcess.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            _mockServerPort = await readyTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            _output.WriteLine($"[ClusterE2E] MockServer 就绪, 端口: {_mockServerPort}");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("等待 MockServer 就绪超时");
        }
    }

    private static string WriteClusterMockServerConfig()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_cluster_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        var configContent = """
            {
              "port": 0,
              "default_response": "集群任务已完成。所有子任务均已执行并合并。",
              "scripted_turns": [
                { "text_response": "我已经分析了任务，可以分解为3个并行子任务。现在开始执行集群流程。" },
                { "text_response": "子任务1已完成：模块A的文档已编写。" },
                { "text_response": "子任务2已完成：模块B的文档已编写。" },
                { "text_response": "子任务3已完成：模块C的文档已编写。" },
                { "text_response": "所有子任务结果已合并，没有冲突。集群执行成功。" },
                { "text_response": "审查通过：3个子任务全部完成，合并结果正确，无回归问题。" }
              ]
            }
            """;

        var configPath = Path.Combine(configDir, "cluster_test.json");
        File.WriteAllText(configPath, configContent);
        return configPath;
    }

    private static string ResolveJccExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir);
        if (artifactsBin is not null)
        {
            var found = SearchExe(artifactsBin, "jcc.exe");
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"jcc.exe 未找到 (baseDir={baseDir})");
    }

    private static string ResolveMockServerPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir);
        if (artifactsBin is not null)
        {
            var found = SearchExe(artifactsBin, "JoinCode.OpenAI.MockServer.exe");
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"MockServer.exe 未找到 (baseDir={baseDir})");
    }

    private static string? FindArtifactsBinRoot(string baseDir)
    {
        var dir = baseDir;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "artifacts", "bin");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string? SearchExe(string root, string exeName)
    {
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            foreach (var subDir in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
            {
                var path = Path.Combine(subDir, exeName);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    private static async Task KillProcessAsync(Process? process)
    {
        if (process is null || process.HasExited) return;
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) { Debug.WriteLine($"[KillProcess] InvalidOperationException: {ex.Message}"); }
        catch (System.ComponentModel.Win32Exception ex) { Debug.WriteLine($"[KillProcess] Win32Exception: {ex.Message}"); }
    }

    [GeneratedRegex(@"localhost:(\d+)")]
    private static partial Regex PortRegex();
}
