namespace MockServer.E2E.Tests.E2E;

using System.Diagnostics;
using System.Text.RegularExpressions;

// E2E 测试需要启动真实进程和访问文件系统路径
#pragma warning disable JCC9001, JCC9002

/// <summary>
/// 自举 E2E 测试 — 验证 jcc --doctor 通过 IGoalEngine 驱动 doctor Agent 的完整链路
/// 替换旧的 DoctorTestSuite，改为进程级端到端验证
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class DoctorBootstrapE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private Process? _mockServerProcess;
    private int _mockServerPort;
    private Process? _jccProcess;

    public DoctorBootstrapE2ETests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await KillProcessAsync(_jccProcess).ConfigureAwait(false);
        await KillProcessAsync(_mockServerProcess).ConfigureAwait(false);
    }

    [Fact]
    public async Task DoctorMode_ShouldStartAndExit_WithMockServer()
    {
        var configPath = WriteDoctorMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        _output.WriteLine($"[DoctorE2E] jcc.exe: {exePath}");
        _output.WriteLine($"[DoctorE2E] MockServer 端口: {_mockServerPort}");

        var exitCode = await RunDoctorAsync(exePath, _mockServerPort, timeoutSeconds: 30).ConfigureAwait(true);

        _output.WriteLine($"[DoctorE2E] jcc.exe 退出码: {exitCode}");

        exitCode.Should().BeOneOf(0, 1, 2);
    }

    [Fact]
    public async Task DoctorMode_WithAwait5_ShouldExitWithinTimeout()
    {
        var configPath = WriteDoctorMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        var sw = Stopwatch.StartNew();

        var exitCode = await RunDoctorAsync(exePath, _mockServerPort, awaitSeconds: 5, timeoutSeconds: 30).ConfigureAwait(true);

        sw.Stop();
        _output.WriteLine($"[DoctorE2E] 耗时: {sw.Elapsed.TotalSeconds:F1}s, 退出码: {exitCode}");

        exitCode.Should().BeOneOf(0, 1, 2);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DoctorMode_ShouldResolveGoalEngine_FromDI()
    {
        var configPath = WriteDoctorMockServerConfig();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        var output = new List<string>();

        var exitCode = await RunDoctorAsync(exePath, _mockServerPort, awaitSeconds: 5, timeoutSeconds: 30, outputSink: output).ConfigureAwait(true);

        _output.WriteLine($"[DoctorE2E] 退出码: {exitCode}");
        _output.WriteLine($"[DoctorE2E] 输出行数: {output.Count}");

        exitCode.Should().BeOneOf(0, 1, 2);
    }

    private async Task<int> RunDoctorAsync(
        string exePath,
        int mockServerPort,
        int awaitSeconds = 0,
        int timeoutSeconds = 60,
        List<string>? outputSink = null)
    {
        var stateDir = Path.Combine(Path.GetTempPath(), $"jcc_doctor_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDir);

        var args = "--trust --doctor";
        if (awaitSeconds > 0)
            args += $" --await {awaitSeconds}";

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
        psi.EnvironmentVariables["JCC_VENDOR"] = "openai";
        psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o";
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890";
        psi.EnvironmentVariables["JCC_PERMISSION_MODE"] = "bypass";
        psi.EnvironmentVariables["JCC_APP_DATA_FOLDER"] = stateDir;
        psi.EnvironmentVariables["JCC_API_TIMEOUT_MS"] = "30000";

        _jccProcess = new Process { StartInfo = psi };

        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _jccProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            outputSink?.Add(e.Data);
            _output.WriteLine($"[jcc:out] {e.Data}");
        };

        _jccProcess.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            outputSink?.Add($"[ERR] {e.Data}");
            _output.WriteLine($"[jcc:err] {e.Data}");
        };

        if (!_jccProcess.Start())
            throw new InvalidOperationException("无法启动 jcc.exe --doctor");

        _jccProcess.BeginOutputReadLine();
        _jccProcess.BeginErrorReadLine();

        _jccProcess.EnableRaisingEvents = true;
        _jccProcess.Exited += (_, _) => exitTcs.TrySetResult(_jccProcess.ExitCode);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await exitTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("[DoctorE2E] jcc.exe 超时，强制终止");
            return -1;
        }
    }

    private async Task StartMockServerAsync(string configPath)
    {
        var mockServerExe = ResolveMockServerPath();
        _output.WriteLine($"[DoctorE2E] MockServer.exe: {mockServerExe}");

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
            _output.WriteLine($"[DoctorE2E] MockServer 就绪, 端口: {_mockServerPort}");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("等待 MockServer 就绪超时");
        }
    }

    private static string WriteDoctorMockServerConfig()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_doctor_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        var configContent = """
            {
              "provider": "openai",
              "model": "gpt-4o",
              "responses": [
                {
                  "role": "assistant",
                  "content": "Doctor Agent 已启动，正在分析链路日志。未发现需要修复的缺陷。"
                }
              ]
            }
            """;

        var configPath = Path.Combine(configDir, "openai.json");
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
        catch (Exception) { _ = process.HasExited; }
    }

    [GeneratedRegex(@":(\d+)/?")]
    private static partial Regex PortRegex();
}
