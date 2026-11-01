namespace MockServer.Core;

/// <summary>
/// MockServer 控制面模式
/// </summary>
public enum MockServerMode {
    /// <summary>回放模式 — 按脚本/录制文件回放，不连真实 LLM</summary>
    Replay,
    /// <summary>录制模式 — 透传真实 LLM + 录下交互</summary>
    Recording,
    /// <summary>交互模式 — 注入优先，无注入则走脚本</summary>
    Interactive
}

/// <summary>
/// 注入的响应 — 通过 /__control__/inject 注入，下次 LLM 请求直接返回此响应
/// </summary>
public sealed class InjectedResponse {
    /// <summary>HTTP 状态码，默认 200</summary>
    public int StatusCode { get; set; } = 200;
    /// <summary>Content-Type，默认 application/json</summary>
    public string ContentType { get; set; } = "application/json";
    /// <summary>响应体（非流式时使用）</summary>
    public string Body { get; set; } = "{}";
    /// <summary>是否流式响应</summary>
    public bool IsStream { get; set; }
    /// <summary>流式分片（IsStream=true 时使用，每片按 SSE 格式发送）</summary>
    public List<string> StreamChunks { get; set; } = [];
}

/// <summary>
/// 控制面状态快照
/// </summary>
public sealed class ControlState {
    /// <summary>当前模式</summary>
    public required MockServerMode Mode { get; init; }
    /// <summary>已捕获的请求总数</summary>
    public required int TotalRequests { get; init; }
    /// <summary>注入队列长度</summary>
    public required int InjectQueueLength { get; init; }
    /// <summary>录制中的交互数</summary>
    public required int RecordedInteractions { get; init; }
    /// <summary>录制上游端点（录制模式时非空）</summary>
    public string? RecordingUpstream { get; init; }
}

/// <summary>
/// 录制启动请求
/// </summary>
public sealed class RecordStartRequest {
    /// <summary>真实 LLM 上游端点（如 https://api.openai.com/v1）</summary>
    public required string Upstream { get; init; }
    /// <summary>API Key（透传给上游）</summary>
    public string? ApiKey { get; init; }
    /// <summary>供应商协议（openai/anthropic/responses）</summary>
    public string Vendor { get; set; } = "openai";
}

/// <summary>
/// 录制停止结果
/// </summary>
public sealed class RecordingResult {
    /// <summary>录制的交互数</summary>
    public required int InteractionCount { get; init; }
    /// <summary>录制文件路径（如已保存）</summary>
    public string? FilePath { get; init; }
}

/// <summary>
/// 录制的单次交互（请求 + 响应）
/// </summary>
public sealed class RecordedInteraction {
    /// <summary>请求索引</summary>
    public required int Index { get; init; }
    /// <summary>请求方法</summary>
    public required string Method { get; init; }
    /// <summary>请求路径</summary>
    public required string Path { get; init; }
    /// <summary>请求体</summary>
    public required string RequestBody { get; init; }
    /// <summary>响应状态码</summary>
    public required int StatusCode { get; init; }
    /// <summary>响应体</summary>
    public required string ResponseBody { get; init; }
    /// <summary>响应 Content-Type</summary>
    public required string ResponseContentType { get; init; }
}

/// <summary>
/// 录制文件格式 — 可序列化保存和加载
/// </summary>
public sealed class RecordingFile {
    /// <summary>录制时间</summary>
    public string CreatedAt { get; set; } = "";
    /// <summary>上游端点</summary>
    public string Upstream { get; set; } = "";
    /// <summary>供应商</summary>
    public string Vendor { get; set; } = "openai";
    /// <summary>录制的交互列表</summary>
    public List<RecordedInteraction> Interactions { get; set; } = [];
}

/// <summary>
/// MockServer 控制器 — 管理注入队列、录制状态和模式切换。
/// 线程安全，由 KestrelMockServer 持有。
/// </summary>
public sealed class MockServerController {
    private readonly Queue<InjectedResponse> _injectQueue = new();
    private readonly List<RecordedInteraction> _recordedInteractions = [];
    private readonly object _lock = new();
    private MockServerMode _mode = MockServerMode.Interactive;
    private string? _recordingUpstream;
    private string? _recordingApiKey;
    private string _recordingVendor = "openai";

    /// <summary>当前模式</summary>
    public MockServerMode Mode {
        get { lock (_lock) return _mode; }
    }

    /// <summary>注入一个响应到队列尾部</summary>
    public void Inject(InjectedResponse response) {
        ArgumentNullException.ThrowIfNull(response);
        lock (_lock) {
            _injectQueue.Enqueue(response);
        }
    }

    /// <summary>消费一个注入的响应（FIFO），无注入返回 null</summary>
    public InjectedResponse? ConsumeInjection() {
        lock (_lock) {
            return _injectQueue.Count > 0 ? _injectQueue.Dequeue() : null;
        }
    }

    /// <summary>清空注入队列</summary>
    public void ClearInjections() {
        lock (_lock) {
            _injectQueue.Clear();
        }
    }

    /// <summary>开始录制模式</summary>
    public void StartRecording(RecordStartRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        lock (_lock) {
            _mode = MockServerMode.Recording;
            _recordingUpstream = request.Upstream;
            _recordingApiKey = request.ApiKey;
            _recordingVendor = request.Vendor;
            _recordedInteractions.Clear();
        }
    }

    /// <summary>停止录制，返回结果</summary>
    public RecordingResult StopRecording() {
        lock (_lock) {
            var count = _recordedInteractions.Count;
            _mode = MockServerMode.Interactive;
            _recordingUpstream = null;
            _recordingApiKey = null;
            return new RecordingResult { InteractionCount = count };
        }
    }

    /// <summary>录制一次交互（录制模式时调用）</summary>
    public void RecordInteraction(RecordedInteraction interaction) {
        ArgumentNullException.ThrowIfNull(interaction);
        lock (_lock) {
            if (_mode == MockServerMode.Recording)
                _recordedInteractions.Add(interaction);
        }
    }

    /// <summary>获取录制上游信息</summary>
    public (string? Upstream, string? ApiKey, string Vendor) GetRecordingConfig() {
        lock (_lock) {
            return (_recordingUpstream, _recordingApiKey, _recordingVendor);
        }
    }

    /// <summary>获取所有录制的交互</summary>
    public IReadOnlyList<RecordedInteraction> GetRecordedInteractions() {
        lock (_lock) {
            return _recordedInteractions.ToList();
        }
    }

    /// <summary>加载录制文件进入回放模式</summary>
    public void LoadReplay(RecordingFile file) {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock) {
            _mode = MockServerMode.Replay;
            _injectQueue.Clear();
            foreach (var interaction in file.Interactions) {
                _injectQueue.Enqueue(new InjectedResponse {
                    StatusCode = interaction.StatusCode,
                    ContentType = interaction.ResponseContentType,
                    Body = interaction.ResponseBody
                });
            }
        }
    }

    /// <summary>切换到交互模式</summary>
    public void SetInteractiveMode() {
        lock (_lock) {
            _mode = MockServerMode.Interactive;
        }
    }

    /// <summary>获取控制面状态快照</summary>
    public ControlState GetState(int totalRequests) {
        lock (_lock) {
            return new ControlState {
                Mode = _mode,
                TotalRequests = totalRequests,
                InjectQueueLength = _injectQueue.Count,
                RecordedInteractions = _recordedInteractions.Count,
                RecordingUpstream = _recordingUpstream
            };
        }
    }

    /// <summary>保存录制到文件</summary>
    public async Task<string> SaveRecordingAsync(string dir) {
        var (interactions, upstream, vendor) = GetRecordingSnapshot();

        Directory.CreateDirectory(dir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(dir, $"recording_{timestamp}.json");

        var file = new RecordingFile {
            CreatedAt = DateTime.UtcNow.ToString("O"),
            Upstream = upstream,
            Vendor = vendor,
            Interactions = interactions
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(file, MockServerJsonContext.Default.RecordingFile);
        await File.WriteAllBytesAsync(filePath, json);
        return filePath;
    }

    private (List<RecordedInteraction> Interactions, string Upstream, string Vendor) GetRecordingSnapshot() {
        lock (_lock) {
            return (_recordedInteractions.ToList(), _recordingUpstream ?? "", _recordingVendor);
        }
    }
}
