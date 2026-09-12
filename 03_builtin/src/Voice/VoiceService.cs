
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
public sealed partial class VoiceService : ActorBase<IVoiceCommand, Unit>, IVoiceService, JoinCode.Abstractions.Interfaces.IVoiceService, IDisposable
{
    private readonly VoiceOptions _options;
    private readonly IResilientHttpClientProvider _resilientProvider;
    private readonly ILogger<VoiceService>? _logger;
    private readonly IClockService _clock;
    private readonly IFileSystem _fs;

    private volatile int _stateInt = (int)VoiceRecordingState.Idle;
    private MemoryStream? _recordingStream;
    private CancellationTokenSource? _recordingCts;
    private DateTime _recordingStartTime;

    public bool IsRecording => (VoiceRecordingState)_stateInt == VoiceRecordingState.Recording;
    public VoiceRecordingState State => (VoiceRecordingState)_stateInt;
    public event EventHandler<VoiceRecordingState>? StateChanged;

    public VoiceService(
        VoiceOptions options,
        IFileSystem fs,
        IResilientHttpClientProvider resilientProvider,
        ILogger<VoiceService>? logger = null,
        IClockService? clock = null)
        : base()
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(resilientProvider);
        _options = options;
        _fs = fs;
        _resilientProvider = resilientProvider;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StartRecordingCmd(cancellationToken, tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<VoiceRecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new StopRecordingCmd(cancellationToken, tcs), cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);

        return _options.Backend switch
        {
            SttBackend.WhisperApi => await TranscribeWithWhisperApiAsync(audioData, language, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException(L.T(StringKey.VoiceUnsupportedSttBackend, _options.Backend))
        };
    }

    /// <inheritdoc/>
    public async Task<string> TranscribeFileAsync(string filePath, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!_fs.FileExists(filePath))
        {
            throw new FileNotFoundException(L.T(StringKey.VoiceAudioFileNotFound), filePath);
        }

        var audioData = await _fs.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await TranscribeAsync(audioData, language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _recordingStream/_recordingCts，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(IVoiceCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case StartRecordingCmd cmd:
                if ((VoiceRecordingState)_stateInt == VoiceRecordingState.Recording)
                {
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
                try
                {
                    if ((VoiceRecordingState)_stateInt != VoiceRecordingState.Recording)
                    {
                        cmd.Tcs.TrySetResult(new VoiceRecordingResult
                        {
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

                    if (audioData.Length == 0)
                    {
                        SetState(VoiceRecordingState.Idle);
                        cmd.Tcs.TrySetResult(new VoiceRecordingResult
                        {
                            Success = false,
                            AudioData = audioData,
                            Duration = duration,
                            ErrorMessage = L.T(StringKey.VoiceRecordingDataEmpty)
                        });
                        break;
                    }

                    string? transcription = null;
                    try
                    {
                        transcription = await TranscribeAsync(audioData, _options.WhisperLanguage, cmd.Ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, L.T(StringKey.VoiceTranscriptionFailed));
                    }

                    SetState(VoiceRecordingState.Idle);
                    _logger?.LogInformation(L.T(StringKey.VoiceRecordingComplete, duration.TotalMilliseconds, transcription?.Length ?? 0));

                    cmd.Tcs.TrySetResult(new VoiceRecordingResult
                    {
                        Success = true,
                        AudioData = audioData,
                        Duration = duration,
                        Transcription = transcription
                    });
                }
                catch (Exception ex)
                {
                    SetState(VoiceRecordingState.Error);
                    cmd.Tcs.TrySetResult(new VoiceRecordingResult
                    {
                        Success = false,
                        AudioData = Array.Empty<byte>(),
                        Duration = TimeSpan.Zero,
                        ErrorMessage = ex.Message
                    });
                }
                break;

            case WriteAudioCmd cmd:
                if (_recordingStream != null)
                {
                    GenerateSilenceBuffer(cmd.Buffer, _options.SampleRate);
                    await _recordingStream.WriteAsync(cmd.Buffer, cmd.Buffer.Length == 0 ? default : CancellationToken.None).ConfigureAwait(false);
                }
                cmd.Tcs.TrySetResult();
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "Voice Actor Consumer 命令处理异常");
    }

    private async Task<string> TranscribeWithWhisperApiAsync(byte[] audioData, string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.WhisperApiKey))
        {
            throw new InvalidOperationException("未配置 Whisper API Key，请在配置文件中设置 voice.whisperApiKey");
        }

        using var content = new MultipartFormDataContent();
        using var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

        content.Add(audioContent, "file", "recording.wav");
        content.Add(new StringContent(_options.WhisperModel), "model");

        var lang = language ?? _options.WhisperLanguage;
        if (!string.IsNullOrEmpty(lang))
        {
            content.Add(new StringContent(lang), "language");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _options.WhisperApiEndpoint) { Content = content };

        if (!string.IsNullOrEmpty(_options.WhisperApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.WhisperApiKey);
        }

        var response = await _resilientProvider.SendResilientAsync(request, "Voice.WhisperApi", cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError(L.T(StringKey.VoiceWhisperApiFailed, response.StatusCode), responseBody);
            throw new InvalidOperationException(L.T(StringKey.VoiceWhisperApiCallFailed, response.StatusCode));
        }

        var result = RelaxedJsonSerializer.Deserialize(responseBody, VoiceJsonContext.Default.WhisperTranscriptionResponse);
        return result?.Text ?? string.Empty;
    }

    private async Task RecordLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var tcs = CreateTcs();
                await SendAsync(new WriteAudioCmd(buffer, tcs), cancellationToken).ConfigureAwait(false);
                await tcs.Task.ConfigureAwait(false);

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VoiceRecordLoopError));
            SetState(VoiceRecordingState.Error);
        }
    }

    private static void GenerateSilenceBuffer(byte[] buffer, int sampleRate)
    {
        var bytesPerSample = 2;
        var samplesPerMs = sampleRate / 1000;
        var bytesToFill = Math.Min(buffer.Length, samplesPerMs * 100 * bytesPerSample);

        for (var i = 0; i < bytesToFill; i++)
        {
            buffer[i] = 0;
        }
    }

    private void SetState(VoiceRecordingState newState)
    {
        _stateInt = (int)newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingStream?.Dispose();
        _ = DisposeAsync();
    }

}

public sealed partial class WhisperTranscriptionResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed partial class WhisperTranscriptionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "whisper-1";

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}
