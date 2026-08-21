namespace MockServer.E2E.Tests.E2E;

using System.Diagnostics;
using System.Text.RegularExpressions;

#pragma warning disable JCC9001, JCC9002

/// <summary>
/// 流式响应 E2E 测试 — 验证 jcc -p 非交互模式通过 MockServer 完成流式请求不卡住
/// TDD: 先写失败测试（jcc --await 20 超时返回 1234），修复后应返回 0
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class StreamingResponseE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private Process? _mockServerProcess;
    private int _mockServerPort;
    private Process? _jccProcess;

    public StreamingResponseE2ETests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await KillProcessAsync(_jccProcess).ConfigureAwait(false);
        await KillProcessAsync(_mockServerProcess).ConfigureAwait(false);
    }

    /// <summary>
    /// jcc -p "echo hello" 连接 MockServer 应在 20s 内正常退出（退出码 0），不应超时（退出码 1234）
    /// </summary>
    [Fact]
    public async Task NonInteractiveMode_ShouldCompleteWithoutTimeout()
    {
        var configPath = WriteSimpleMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        _output.WriteLine($"[StreamE2E] jcc.exe: {exePath}");
        _output.WriteLine($"[StreamE2E] MockServer 端口: {_mockServerPort}");

        var (exitCode, elapsed) = await RunJccAsync(exePath, _mockServerPort, prompt: "echo hello", awaitSeconds: 20, timeoutSeconds: 30).ConfigureAwait(true);

        _output.WriteLine($"[StreamE2E] jcc.exe 退出码: {exitCode}, 耗时: {elapsed.TotalSeconds:F1}s");

        exitCode.Should().Be(0, "jcc 应正常完成，不应超时（1234）");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25), "应在 --await 20s 超时前完成");
    }

    /// <summary>
    /// jcc -p 发送请求后 MockServer 应收到至少 1 个请求
    /// </summary>
    [Fact]
    public async Task NonInteractiveMode_MockServerShouldReceiveRequest()
    {
        var configPath = WriteSimpleMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();

        var (exitCode, _) = await RunJccAsync(exePath, _mockServerPort, prompt: "echo hello", awaitSeconds: 20, timeoutSeconds: 30).ConfigureAwait(true);

        _output.WriteLine($"[StreamE2E] jcc.exe 退出码: {exitCode}");

        var dumpDir = Path.Combine(Path.GetDirectoryName(ResolveMockServerPath())!, "tests", "MockServers", "MockServer.Core", "dumps", "OpenAI");
        if (Directory.Exists(dumpDir))
        {
            var recentDumps = Directory.GetFiles(dumpDir, "req_*.txt")
                .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddMinutes(-2))
                .ToList();
            _output.WriteLine($"[StreamE2E] 最近 dump 文件数: {recentDumps.Count}");
            recentDumps.Count.Should().BeGreaterThanOrEqualTo(1, "MockServer 应收到至少 1 个请求");
        }
    }

    private async Task<(int ExitCode, TimeSpan Elapsed)> RunJccAsync(
        string exePath,
        int mockServerPort,
        string prompt,
        int awaitSeconds = 20,
        int timeoutSeconds = 60)
    {
        var stateDir = Path.Combine(Path.GetTempPath(), $"jcc_stream_e2e_{Guid.NewGuid():N}");
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
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890";
        psi.EnvironmentVariables["JCC_VENDOR"] = "openai";
        psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o";
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890";
        psi.EnvironmentVariables["JCC_PERMISSION_MODE"] = "bypass";
        psi.EnvironmentVariables["JCC_APP_DATA_FOLDER"] = stateDir;
        psi.EnvironmentVariables["JCC_API_TIMEOUT_MS"] = "15000";

        _jccProcess = new Process { StartInfo = psi };

        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _jccProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _output.WriteLine($"[jcc:out] {e.Data}");
        };

        _jccProcess.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
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
            return (code, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _output.WriteLine("[StreamE2E] jcc.exe 超时，强制终止");
            return (-1, sw.Elapsed);
        }
    }

    private async Task StartMockServerAsync(string configPath)
    {
        var mockServerExe = ResolveMockServerPath();
        _output.WriteLine($"[StreamE2E] MockServer.exe: {mockServerExe}");

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
            _output.WriteLine($"[StreamE2E] MockServer 就绪, 端口: {_mockServerPort}");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("等待 MockServer 就绪超时");
        }
    }

    private static string WriteSimpleMockServerConfig()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_stream_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        var configContent = """
            {
              "port": 0,
              "default_response": "Hello from MockServer!",
              "scripted_turns": [
                {
                  "text_response": "Hello! I received your message."
                }
              ]
            }
            """;

        var configPath = Path.Combine(configDir, "stream_test.json");
        IO.FileSystem.SafeFileIO.WriteAllText(configPath, configContent);
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
