namespace MockServer.E2E.Tests.Core;

/// <summary>
/// 共享 MockServer 进程的 xUnit ICollectionFixture — 所有 E2E 测试复用同一个 MockServer 进程和端口。
/// 每个测试前通过 ReplaceScriptAsync 加载自己的脚本,避免每个测试启动/关闭进程的开销和端口浪费。
/// 同一 Collection 内的测试串行执行(xUnit 默认),保证脚本不被并发消费干扰。
/// </summary>
public sealed class MockServerSharedFixture : IAsyncLifetime {
    private Process? _mockServerProcess;
    private int _port;
    private readonly SemaphoreSlim _replaceLock = new(1, 1);

    /// <summary>共享 MockServer 的基础 URL (http://localhost:{port})。</summary>
    public string BaseUrl => $"http://localhost:{_port}";

    /// <summary>共享 MockServer 的端口。</summary>
    public int Port => _port;

    /// <summary>是否已初始化成功。</summary>
    public bool IsReady => _mockServerProcess is not null && !_mockServerProcess.HasExited && _port > 0;

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

        _mockServerProcess.OutputDataReceived += (_, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            var idx = e.Data.IndexOf(readyMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) {
                var urlPart = e.Data[(idx + readyMarker.Length)..].Trim();
                var match = Regex.Match(urlPart, @":(\d+)/?");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    readyTcs.TrySetResult(port);
            }
        };

        _mockServerProcess.ErrorDataReceived += (_, e) => { };

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
            var content = new StringContent(configJson, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{BaseUrl}/__control__/replace_script", content, ct).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
        } finally {
            _replaceLock.Release();
        }
    }

    public async Task DisposeAsync() {
        if (_mockServerProcess is not null && !_mockServerProcess.HasExited) {
            try {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                await http.GetAsync($"{BaseUrl}/shutdown", CancellationToken.None).ConfigureAwait(true);
            } catch {
                try { KillMockServerProcess(); } catch { }
            }
            _mockServerProcess.WaitForExit(5000);
        }
        _mockServerProcess?.Dispose();
        _replaceLock.Dispose();
    }

    private void KillMockServerProcess() {
        if (_mockServerProcess is not null && !_mockServerProcess.HasExited)
            _mockServerProcess.Kill(entireProcessTree: true);
    }

    private static async Task<string> WriteEmptyConfigAsync() {
        var configDir = Path.Combine(Path.GetTempPath(), $"jcc_shared_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "mockserver.json");
        var configJson = """
        {
          "port": 0,
          "default_response": "(shared mock server - script not loaded)",
          "scripted_turns": []
        }
        """;
        await File.WriteAllTextAsync(configPath, configJson).ConfigureAwait(true);
        return configPath;
    }

    private static string ResolveMockServerPath() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            var candidate = Path.Combine(dir, "artifacts", "bin", "OpenAI.MockServer");
            if (Directory.Exists(candidate)) {
                var exeName = OperatingSystem.IsWindows() ? "JoinCode.OpenAI.MockServer.exe" : "JoinCode.OpenAI.MockServer";
                var exe = Directory.GetFiles(candidate, exeName, SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("Release"))
                    .FirstOrDefault()
                    ?? throw new FileNotFoundException($"MockServer exe 未编译", candidate);
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
