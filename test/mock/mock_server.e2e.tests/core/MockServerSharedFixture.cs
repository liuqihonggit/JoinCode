namespace MockServer.E2E.Tests.Core;

/// <summary>
/// 共享 MockServer 进程的 xUnit ICollectionFixture — 所有 E2E 测试复用同一个 MockServer 进程和端口。
/// 每个测试前通过 ReplaceScriptAsync 加载自己的脚本,避免每个测试启动/关闭进程的开销和端口浪费。
/// 同一 Collection 内的测试串行执行(xUnit 默认),保证脚本不被并发消费干扰。
/// </summary>
public sealed class MockServerSharedFixture : IAsyncLifetime {
    private readonly IFileSystem _fs;
    private Process? _mockServerProcess;
    private int _port;
    private readonly SemaphoreSlim _replaceLock = new(1, 1);
    private DataReceivedEventHandler? _onOutput;
    private DataReceivedEventHandler? _onError;

    /// <summary>共享 MockServer 的基础 URL (http://localhost:{port})。</summary>
    public string BaseUrl => $"http://localhost:{_port}";

    /// <summary>共享 MockServer 的端口。</summary>
    public int Port => _port;

    /// <summary>是否已初始化成功。</summary>
    public bool IsReady => _mockServerProcess is not null && !_mockServerProcess.HasExited && _port > 0;

    public MockServerSharedFixture() {
        _fs = new IO.FileSystem.PhysicalFileSystem();
    }

    public async Task InitializeAsync() {
        var exe = ResolveMockServerPath();
        var configPath = await WriteEmptyConfigAsync();

        var startInfo = new ProcessStartInfo {
            FileName = exe,
            Arguments = $"--config \"{configPath}\" --port 0 --no-open-browser",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
        };

        _mockServerProcess = new Process { StartInfo = startInfo };
        var readyTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string readyMarker = "[OpenAI]   URL:";

        _onOutput = (_, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) {
                var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
                var match = Regex.Match(urlPart, @":(\d+)/?");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    readyTcs.TrySetResult(port);
            }
        };
        _onError = (_, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine($"[MockServer] {e.Data}");
        };

        _mockServerProcess.OutputDataReceived += _onOutput;
        _mockServerProcess.ErrorDataReceived += _onError;

        if (!_mockServerProcess.Start())
            throw new InvalidOperationException("[GEN040] 无法启动共享 MockServer 进程");

        _mockServerProcess.BeginOutputReadLine();
        _mockServerProcess.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try {
            _port = await readyTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
        } catch (OperationCanceledException) {
            throw new InvalidOperationException("[GEN041] 等待共享 MockServer 就绪超时(25s)");
        }

        await WaitForKestrelListenAsync(cts.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// 等待 Kestrel 真正开始监听端口 — URL 标记可能在 Kestrel.StartAsync 完成前输出,
    /// 导致首个测试 POST 时 Connection refused。用健康检查重试确认监听就绪。
    /// </summary>
    private async Task WaitForKestrelListenAsync(CancellationToken ct) {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 30; attempt++) {
            try {
                using var resp = await http.GetAsync($"{BaseUrl}/health", ct).ConfigureAwait(true);
                if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return;
            } catch (HttpRequestException) {
                await Task.Delay(200, ct).ConfigureAwait(true);
            }
        }
        throw new InvalidOperationException("[GEN043] 共享 MockServer Kestrel 监听就绪超时(6s)");
    }

    /// <summary>
    /// 动态替换 MockServer 脚本 — 通过 POST /__control__/replace_script 端点加载新脚本。
    /// 线程安全:用 SemaphoreSlim 串行化替换操作,避免并发替换竞争。
    /// </summary>
    public async Task ReplaceScriptAsync(string configJson, CancellationToken ct = default) {
        if (!IsReady)
            throw new InvalidOperationException("[GEN042] 共享 MockServer 未就绪");

        await _replaceLock.WaitAsync(ct).ConfigureAwait(true);
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var content = new StringContent(configJson, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{BaseUrl}/__control__/replace_script", content, ct).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
        } finally {
            _replaceLock.Release();
        }
    }

    public async Task DisposeAsync() {
        if (_mockServerProcess is not null) {
            if (_onOutput is not null)
                _mockServerProcess.OutputDataReceived -= _onOutput;
            if (_onError is not null)
                _mockServerProcess.ErrorDataReceived -= _onError;

            if (!_mockServerProcess.HasExited) {
                try {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    await http.GetAsync($"{BaseUrl}/shutdown", CancellationToken.None).ConfigureAwait(true);
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[MockServer] shutdown 失败: {ex.Message}");
                    try { KillMockServerProcess(); } catch (Exception ex2) {
                        Console.Error.WriteLine($"[MockServer] kill 失败: {ex2.Message}");
                    }
                }
                _mockServerProcess.WaitForExit(5000);
            }
            _mockServerProcess.Dispose();
        }
        _replaceLock.Dispose();
    }

    private void KillMockServerProcess() {
        if (_mockServerProcess is not null && !_mockServerProcess.HasExited)
            _mockServerProcess.Kill(entireProcessTree: true);
    }

    private async Task<string> WriteEmptyConfigAsync() {
        var configDir = _fs.CombinePath(Path.GetTempPath(), $"jcc_shared_mock_{Guid.NewGuid():N}");
        _fs.CreateDirectory(configDir);
        var configPath = _fs.CombinePath(configDir, "mockserver.json");
        var configJson = """
        {
          "port": 0,
          "default_response": "(shared mock server - script not loaded)",
          "scripted_turns": []
        }
        """;
        await _fs.WriteAllTextAsync(configPath, configJson).ConfigureAwait(true);
        return configPath;
    }

    private string ResolveMockServerPath() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            var candidate = _fs.CombinePath(dir, "artifacts", "bin", "OpenAI.MockServer");
            if (_fs.DirectoryExists(candidate)) {
                var exeName = OperatingSystem.IsWindows() ? "JoinCode.OpenAI.MockServer.exe" : "JoinCode.OpenAI.MockServer";
                var exe = _fs.GetFiles(candidate, exeName, SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("Release"))
                    .FirstOrDefault();
                if (exe is null)
                    throw new FileNotFoundException("MockServer exe 未编译", candidate);
                return exe;
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new InvalidOperationException("未找到 artifacts/bin/OpenAI.MockServer 目录");
    }
}

/// <summary>
/// xUnit 集合定义 — 关联 "MockServerShared" 集合与 MockServerSharedFixture。
/// 所有标记 [Collection("MockServerShared")] 的测试类共享同一个 MockServer 进程。
/// </summary>
[CollectionDefinition("MockServerShared")]
public sealed class MockServerSharedCollection : ICollectionFixture<MockServerSharedFixture> { }
