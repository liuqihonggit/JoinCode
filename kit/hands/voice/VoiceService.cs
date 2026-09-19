
namespace Services.Voice;

/// <summary>
/// Voice Actor 命令 — Channel 中的消息类型
/// </summary>
public interface IVoiceCommand;

internal sealed record StartRecordingCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IVoiceCommand;
internal sealed record StopRecordingCmd(CancellationToken Ct, TaskCompletionSource<VoiceRecordingResult> Tcs) : IVoiceCommand;
internal sealed record WriteAudioCmd(byte[] Buffer, TaskCompletionSource Tcs) : IVoiceCommand;

/// <summary>
/// 语音服务 — Actor 化：继承 ActorBase，Consumer 线程独占 _recordingStream/_recordingCts，
/// 消除 AsyncLock。StopRecording 的 Whisper API 网络调用在 Consumer 中执行，
/// 不再阻塞 RecordLoop 的音频写入命令。
/// 状态用 Volatile.Read 保持同步属性读取，写入由 Consumer 独占。
/// </summary>
[Register(typeof(IVoiceService), ServiceLifetime.Singleton)]
[Register(typeof(JoinCode.Abstractions.Interfaces.IVoiceService), ServiceLifetime.Singleton)]
public sealed partial class VoiceService : ActorBase<IVoiceCommand, Unit>, IVoiceService, JoinCode.Abstractions.Interfaces.IVoiceService, IDisposable {
    private readonly VoiceOptions _options;
    private readonly IResilientHttpClientProvider _resilientProvider;
    private readonly ILogger<VoiceService>? _logger;
    private readonly IClockService _clock;
    private readonly IFileSystem _fs;

    private volatile int _stateInt = (int)VoiceRecordingState.Idle;
    private MemoryStream? _recordingStream;
    private CancellationTokenSource? _recordingCts;
    private DateTime _recordingStartTime;
    private bool _disposed;

    /// <summary>
    /// 获取当前是否正在录制音频。
    /// </summary>
    public bool IsRecording => (VoiceRecordingState)_stateInt == VoiceRecordingState.Recording;

    /// <summary>
    /// 获取当前语音录制状态。
    /// </summary>
    public VoiceRecordingState State => (VoiceRecordingState)_stateInt;

    /// <summary>
    /// 录制状态变更事件 — 状态切换时触发，参数为新的状态值。
    /// </summary>
    public event EventHandler<VoiceRecordingState>? StateChanged;

    /// <summary>
    /// 初始化 <see cref="VoiceService"/> 实例。
    /// </summary>
    /// <param name="options">语音服务配置选项。</param>
    /// <param name="fs">文件系统抽象，用于读写音频文件。</param>
    /// <param name="resilientProvider">弹性 HTTP 客户端提供程序，用于调用 Whisper API。</param>
    /// <param name="logger">可选的日志记录器。</param>
    /// <param name="clock">可选的时钟服务，用于测试时间控制。</param>
    public VoiceService(
        VoiceOptions options,
        IFileSystem fs,
        IResilientHttpClientProvider resilientProvider,
        ILogger<VoiceService>? logger = null,
        IClockService? clock = null)
        : base() {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(resilientProvider);
        _options = options;
        _fs = fs;
        _resilientProvider = resilientProvider;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }


    /// <inheritdoc/>
    public async Task StartRecordingAsync(CancellationToken ct = default) {
        var tcs = TcsFactory.Create();
        await SendAsync(new StartRecordingCmd(ct, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken ct = default) {
        var tcs = new TaskCompletionSource<VoiceRecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new StopRecordingCmd(ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(audioData);

        return _options.Backend switch {
            SttBackend.WhisperApi => await TranscribeWithWhisperApiAsync(audioData, language, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException(L.T(StringKey.VoiceUnsupportedSttBackend, _options.Backend))
        };
    }

    /// <inheritdoc/>
    public async Task<string> TranscribeFileAsync(string filePath, string? language = null, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!_fs.FileExists(filePath)) {
            throw new FileNotFoundException(L.T(StringKey.VoiceAudioFileNotFound), filePath);
        }

        var audioData = await _fs.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        return await TranscribeAsync(audioData, language, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _recordingStream/_recordingCts，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(IVoiceCommand command, CancellationToken ct) {
        switch (command) {
            case StartRecordingCmd cmd:
            if ((VoiceRecordingState)_stateInt == VoiceRecordingState.Recording) {
                _logger?.LogWarning(L.T(StringKey.VoiceAlreadyRecording));
                cmd.Tcs.TrySetResult();
                break;
            }

            _recordingStream = new MemoryStream();
            _recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cmd.Ct);
            _recordingStartTime = _clock.GetUtcNow();

            SetState(VoiceRecordingState.Recording);
            _logger?.LogInformation(L.T(StringKey.VoiceStartRecording));

            _ = Task.Run(() => RecordLoopAsync(_recordingCts.Token));
            cmd.Tcs.TrySetResult();
            break;

            case StopRecordingCmd cmd:
            try {
                if ((VoiceRecordingState)_stateInt != VoiceRecordingState.Recording) {
                    cmd.Tcs.TrySetResult(new VoiceRecordingResult {
                        Success = false,
                        AudioData = Array.Empty<byte>(),
                        Duration = TimeSpan.Zero,
                        ErrorMessage = L.T(StringKey.VoiceNotRecording)
                    });
                    break;
                }

                _recordingCts?.Cancel();
                SetState(VoiceRecordingState.Processing);

                var duration = _clock.GetUtcNow() - _recordingStartTime;
                var audioData = _recordingStream?.ToArray() ?? Array.Empty<byte>();

                _recordingStream?.Dispose();
                _recordingStream = null;

                if (audioData.Length == 0) {
                    SetState(VoiceRecordingState.Idle);
                    cmd.Tcs.TrySetResult(new VoiceRecordingResult {
                        Success = false,
                        AudioData = audioData,
                        Duration = duration,
                        ErrorMessage = L.T(StringKey.VoiceRecordingDataEmpty)
                    });
                    break;
                }

                string? transcription = null;
                try {
                    transcription = await TranscribeAsync(audioData, _options.WhisperLanguage, cmd.Ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogError(ex, L.T(StringKey.VoiceTranscriptionFailed));
                }

                SetState(VoiceRecordingState.Idle);
                _logger?.LogInformation(L.T(StringKey.VoiceRecordingComplete, duration.TotalMilliseconds, transcription?.Length ?? 0));

                cmd.Tcs.TrySetResult(new VoiceRecordingResult {
                    Success = true,
                    AudioData = audioData,
                    Duration = duration,
                    Transcription = transcription
                });
            } catch (Exception ex) {
                SetState(VoiceRecordingState.Error);
                cmd.Tcs.TrySetResult(new VoiceRecordingResult {
                    Success = false,
                    AudioData = Array.Empty<byte>(),
                    Duration = TimeSpan.Zero,
                    ErrorMessage = ex.Message
                });
            }
            break;

            case WriteAudioCmd cmd:
            if (_recordingStream != null) {
                GenerateSilenceBuffer(cmd.Buffer, _options.SampleRate);
                await _recordingStream.WriteAsync(cmd.Buffer, cmd.Buffer.Length == 0 ? default : CancellationToken.None).ConfigureAwait(false);
            }
            cmd.Tcs.TrySetResult();
            break;
        }
    }

    /// <summary>
    /// Actor Consumer 错误回调 — 记录消费者线程未捕获异常。
    /// </summary>
    /// <param name="ex">消费者线程抛出的异常。</param>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogWarning(ex, "Voice Actor Consumer 命令处理异常");
    }

    private async Task<string> TranscribeWithWhisperApiAsync(byte[] audioData, string? language, CancellationToken ct) {
        if (string.IsNullOrEmpty(_options.WhisperApiKey)) {
            throw new InvalidOperationException("未配置 Whisper API Key，请在配置文件中设置 voice.whisperApiKey");
        }

        using var content = new MultipartFormDataContent();
        using var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

        content.Add(audioContent, "file", "recording.wav");
        content.Add(new StringContent(_options.WhisperModel), "model");

        var lang = language ?? _options.WhisperLanguage;
        if (!string.IsNullOrEmpty(lang)) {
            content.Add(new StringContent(lang), "language");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _options.WhisperApiEndpoint) { Content = content };

        if (!string.IsNullOrEmpty(_options.WhisperApiKey)) {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.WhisperApiKey);
        }

        var response = await _resilientProvider.SendResilientAsync(request, "Voice.WhisperApi", ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            _logger?.LogError(L.T(StringKey.VoiceWhisperApiFailed, response.StatusCode), responseBody);
            throw new InvalidOperationException(L.T(StringKey.VoiceWhisperApiCallFailed, response.StatusCode));
        }

        var result = RelaxedJsonSerializer.Deserialize(responseBody, VoiceJsonContext.Default.WhisperTranscriptionResponse);
        return result?.Text ?? string.Empty;
    }

    private async Task RecordLoopAsync(CancellationToken ct) {
        try {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested) {
                var tcs = TcsFactory.Create();
                await SendAsync(new WriteAudioCmd(buffer, tcs), ct).ConfigureAwait(false);
                await AskAwait(tcs, ct).ConfigureAwait(false);

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.VoiceRecordLoopError));
            SetState(VoiceRecordingState.Error);
        }
    }

    private static void GenerateSilenceBuffer(byte[] buffer, int sampleRate) {
        var bytesPerSample = 2;
        var samplesPerMs = sampleRate / 1000;
        var bytesToFill = Math.Min(buffer.Length, samplesPerMs * 100 * bytesPerSample);

        for (var i = 0; i < bytesToFill; i++) {
            buffer[i] = 0;
        }
    }

    private void SetState(VoiceRecordingState newState) {
        _stateInt = (int)newState;
        StateChanged?.Invoke(this, newState);
    }

    /// <summary>
    /// 同步释放录制流和取消令牌等资源。Actor 的异步释放由 <see cref="DisposeAsync"/> 负责。
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingStream?.Dispose();
    }

    /// <summary>
    /// 异步释放资源 — 先停 Actor(基类),再释放录制流和取消令牌。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public override async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingStream?.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

}

/// <summary>
/// Whisper API 转录响应 — 反序列化 Whisper 接口返回的 JSON。
/// </summary>
public sealed partial class WhisperTranscriptionResponse {
    /// <summary>
    /// 转录得到的文本内容。
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Whisper API 转录请求 — 序列化发送至 Whisper 接口的 JSON。
/// </summary>
public sealed partial class WhisperTranscriptionRequest {
    /// <summary>
    /// 使用的 Whisper 模型名称，默认 "whisper-1"。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "whisper-1";

    /// <summary>
    /// 音频语言代码（如 "zh"），为 null 时由 Whisper 自动检测。
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }
}