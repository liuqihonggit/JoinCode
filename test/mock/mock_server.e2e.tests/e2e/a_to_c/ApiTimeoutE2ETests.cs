// JCC11003 抑制: E2E 测试可空抑制
namespace MockServer.E2E.Tests.E2E;

#pragma warning disable JCC9002

/// <summary>
/// LLM API 超时 E2E 测试 — Bug 29：超时场景退出码应为 1240(LlmCallTimeout) 而非 1(GeneralError)
/// 验证链路：慢服务器 + 短超时 → SessionController 兜底判定 → CliSession 抛 TimeoutException → rc=1240
/// </summary>
[Trait("Category", "Integration")]
[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = "E2E 测试需直接访问文件系统启动进程")]
public sealed partial class ApiTimeoutE2ETests : IAsyncLifetime {
    private readonly ITestOutputHelper _output;
    private Process? _mockServerProcess;
    private int _mockServerPort;
    private Process? _jccProcess;
    private DataReceivedEventHandler? _onJccOutput;
    private DataReceivedEventHandler? _onJccError;
    private EventHandler? _onJccExited;
    private DataReceivedEventHandler? _onMockOutput;
    private DataReceivedEventHandler? _onMockError;

    public ApiTimeoutE2ETests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_jccProcess is not null) {
            if (_onJccOutput is not null) _jccProcess.OutputDataReceived -= _onJccOutput;
            if (_onJccError is not null) _jccProcess.ErrorDataReceived -= _onJccError;
            if (_onJccExited is not null) _jccProcess.Exited -= _onJccExited;
        }
        if (_mockServerProcess is not null) {
            if (_onMockOutput is not null) _mockServerProcess.OutputDataReceived -= _onMockOutput;
            if (_onMockError is not null) _mockServerProcess.ErrorDataReceived -= _onMockError;
        }
        await KillProcessAsync(_jccProcess);
        await KillProcessAsync(_mockServerProcess);
    }

    /// <summary>
    /// Bug 29：超时(3s) + 慢服务器(10s) → jcc 应返回 rc=1240(LlmCallTimeout)，耗时 ≈3s
    /// </summary>
    [Fact]
    public async Task ApiTimeout_SlowServer_ShouldReturnLlmCallTimeout_1240() {
        var configPath = await WriteSimpleMockServerConfigAsync();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        _output.WriteLine($"[ApiTimeoutE2E] jcc.exe: {exePath}");
        _output.WriteLine($"[ApiTimeoutE2E] MockServer 端口: {_mockServerPort}");

        await InjectDelayedResponsesAsync(_mockServerPort, delayMs: 10_000, count: 5).ConfigureAwait(true);
        _output.WriteLine("[ApiTimeoutE2E] 已注入 5 个延迟 10s 响应");

        var (exitCode, elapsed) = await RunJccAsync(
            exePath, _mockServerPort, prompt: "hello",
            apiTimeoutMs: 3_000, awaitSeconds: 15, timeoutSeconds: 30).ConfigureAwait(true);

        _output.WriteLine($"[ApiTimeoutE2E] jcc.exe 退出码: {exitCode}, 耗时: {elapsed.TotalSeconds:F1}s");

        exitCode.Should().Be(1240, "超时应返回 LlmCallTimeout(1240)，而非 GeneralError(1)");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "应在 3s 超时后快速返回，不应等满 10s 延迟");
    }

    /// <summary>
    /// 第25条：空响应体不应卡死 30s，应快速返回（空回复或错误）
    /// </summary>
    [Fact]
    public async Task EmptyResponseBody_ShouldNotHang_ShouldReturnQuickly() {
        var configPath = await WriteSimpleMockServerConfigAsync();
        await StartMockServerAsync(configPath).ConfigureAwait(true);

        var exePath = ResolveJccExePath();
        _output.WriteLine($"[ApiTimeoutE2E] jcc.exe: {exePath}");

        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_mockServerPort}") };
        var responses = Enumerable.Repeat(new E2eInjectedResponse { Body = "" }, 5).ToList();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(responses, typeof(List<E2eInjectedResponse>), E2eJsonContext.Default);
        var injectResp = await http.PostAsync("/__control__/script",
            new ByteArrayContent(jsonBytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } }).ConfigureAwait(true);
        injectResp.EnsureSuccessStatusCode();
        _output.WriteLine("[ApiTimeoutE2E] 已注入 5 个空响应体");

        var (exitCode, elapsed) = await RunJccAsync(
            exePath, _mockServerPort, prompt: "hello",
            apiTimeoutMs: 10_000, awaitSeconds: 15, timeoutSeconds: 30).ConfigureAwait(true);

        _output.WriteLine($"[ApiTimeoutE2E] jcc.exe 退出码: {exitCode}, 耗时: {elapsed.TotalSeconds:F1}s");

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "空响应体不应卡死 30s，应快速返回");
    }

    private async Task InjectDelayedResponsesAsync(int port, int delayMs, int count) {
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var responses = Enumerable.Repeat(new E2eInjectedResponse { DelayMs = delayMs, Body = "{}" }, count).ToList();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(responses, typeof(List<E2eInjectedResponse>), E2eJsonContext.Default);
        var response = await http.PostAsync("/__control__/script",
            new ByteArrayContent(jsonBytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } }).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
        _output.WriteLine($"[ApiTimeoutE2E] /__control__/script 响应: {body}");
    }

    private async Task<(int ExitCode, TimeSpan Elapsed)> RunJccAsync(
        string exePath,
        int mockServerPort,
        string prompt,
        int apiTimeoutMs = 30_000,
        int awaitSeconds = 20,
        int timeoutSeconds = 60) {
        var stateDir = Path.Combine(Path.GetTempPath(), $"jcc_timeout_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDir);
        await E2eSettingsJsonHelper.WriteSettingsJsonToStateDirAsync(stateDir);

        var args = $"--trust --await {awaitSeconds} -p \"{prompt}\"";

        var psi = new ProcessStartInfo {
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
        psi.EnvironmentVariables["JCC_PERMISSION_MODE"] = "bypass";
        psi.EnvironmentVariables["JCC_APP_DATA_FOLDER"] = stateDir;
        psi.EnvironmentVariables["JCC_API_TIMEOUT_MS"] = apiTimeoutMs.ToString();
        psi.EnvironmentVariables["JCC_DISABLE_RETRY"] = "1";

        _jccProcess = new Process { StartInfo = psi };

        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _onJccOutput = (_, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            _output.WriteLine($"[jcc:out] {e.Data}");
        };
        _jccProcess.OutputDataReceived += _onJccOutput;

        _onJccError = (_, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            _output.WriteLine($"[jcc:err] {e.Data}");
        };
        _jccProcess.ErrorDataReceived += _onJccError;

        if (!_jccProcess.Start())
            throw new InvalidOperationException("无法启动 jcc.exe");

        _jccProcess.BeginOutputReadLine();
        _jccProcess.BeginErrorReadLine();

        _jccProcess.EnableRaisingEvents = true;
        _onJccExited = (_, _) => exitTcs.TrySetResult(_jccProcess.ExitCode);
        _jccProcess.Exited += _onJccExited;

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            var code = await exitTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            sw.Stop();
            return (code, sw.Elapsed);
        } catch (OperationCanceledException) {
            sw.Stop();
            _output.WriteLine("[ApiTimeoutE2E] jcc.exe 超时，强制终止");
            return (-1, sw.Elapsed);
        }
    }

    private async Task StartMockServerAsync(string configPath) {
        var mockServerExe = ResolveMockServerPath();
        _output.WriteLine($"[ApiTimeoutE2E] MockServer.exe: {mockServerExe}");

        var psi = new ProcessStartInfo {
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

        _onMockOutput = (_, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            _output.WriteLine($"[MockServer:out] {e.Data}");
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) {
                var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
                var match = PortRegex().Match(urlPart);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    readyTcs.TrySetResult(port);
            }
        };
        _mockServerProcess.OutputDataReceived += _onMockOutput;

        _onMockError = (_, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[MockServer:ERR] {e.Data}");
        };
        _mockServerProcess.ErrorDataReceived += _onMockError;

        if (!_mockServerProcess.Start())
            throw new InvalidOperationException("无法启动 MockServer");

        _mockServerProcess.BeginOutputReadLine();
        _mockServerProcess.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try {
            _mockServerPort = await readyTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
            _output.WriteLine($"[ApiTimeoutE2E] MockServer 就绪, 端口: {_mockServerPort}");
        } catch (OperationCanceledException) {
            throw new InvalidOperationException("等待 MockServer 就绪超时");
        }
    }

    private static async Task<string> WriteSimpleMockServerConfigAsync() {
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_timeout_mock_{Guid.NewGuid():N}");
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

        var configPath = Path.Combine(configDir, "timeout_test.json");
        await IO.FileSystem.SafeFileIO.WriteAllText(configPath, configContent);
        return configPath;
    }

    private static string ResolveJccExePath() {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir);
        if (artifactsBin is not null) {
            var found = SearchExe(artifactsBin, "jcc.exe");
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"jcc.exe 未找到 (baseDir={baseDir})");
    }

    private static string ResolveMockServerPath() {
        var baseDir = AppContext.BaseDirectory;
        var artifactsBin = FindArtifactsBinRoot(baseDir);
        if (artifactsBin is not null) {
            var found = SearchExe(artifactsBin, "JoinCode.OpenAI.MockServer.exe");
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"MockServer.exe 未找到 (baseDir={baseDir})");
    }

    private static string? FindArtifactsBinRoot(string baseDir) {
        var dir = baseDir;
        for (var i = 0; i < 10; i++) {
            var candidate = Path.Combine(dir, "artifacts", "bin");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string? SearchExe(string root, string exeName) {
        var linuxName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exeName[..^4]
            : exeName;
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)) {
            foreach (var subDir in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)) {
                var path = Path.Combine(subDir, exeName);
                if (File.Exists(path)) return path;
                var linuxPath = Path.Combine(subDir, linuxName);
                if (File.Exists(linuxPath)) return linuxPath;
            }
        }
        return null;
    }

    private static async Task KillProcessAsync(Process? process) {
        if (process is null || process.HasExited) return;
        try {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
        } catch (InvalidOperationException ex) { Debug.WriteLine($"[KillProcess] InvalidOperationException: {ex.Message}"); } catch (System.ComponentModel.Win32Exception ex) { Debug.WriteLine($"[KillProcess] Win32Exception: {ex.Message}"); }
    }

    [GeneratedRegex(@"localhost:(\d+)")]
    private static partial Regex PortRegex();
}
