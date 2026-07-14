
namespace Services.Voice;

[Register(typeof(IVoiceService))]
[Register(typeof(JoinCode.Abstractions.Interfaces.IVoiceService))]
public sealed partial class VoiceService : IVoiceService, JoinCode.Abstractions.Interfaces.IVoiceService, IDisposable
{
    private readonly VoiceOptions _options;
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<VoiceService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly IFileSystem _fs;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private VoiceRecordingState _state = VoiceRecordingState.Idle;
    private MemoryStream? _recordingStream;
    private CancellationTokenSource? _recordingCts;
    private DateTime _recordingStartTime;

    public bool IsRecording => _state == VoiceRecordingState.Recording;
    public VoiceRecordingState State => _state;
    public event EventHandler<VoiceRecordingState>? StateChanged;

    public VoiceService(
        VoiceOptions options,
        IFileSystem fs,
        IHttpClientProvider httpClientProvider,
        ILogger<VoiceService>? logger = null,
        IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(httpClientProvider);
        _options = options;
        _fs = fs;
        _httpClient = httpClientProvider.GetClient();
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(_options.WhisperApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.WhisperApiKey);
        }
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == VoiceRecordingState.Recording)
            {
                _logger?.LogWarning(L.T(StringKey.VoiceAlreadyRecording));
                return;
            }

            _recordingStream = new MemoryStream();
            _recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _recordingStartTime = _clock.GetUtcNow();

            SetState(VoiceRecordingState.Recording);
            _logger?.LogInformation(L.T(StringKey.VoiceStartRecording));

            _ = RecordLoopAsync(_recordingCts.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != VoiceRecordingState.Recording)
            {
                return new VoiceRecordingResult
                {
                    Success = false,
                    AudioData = Array.Empty<byte>(),
                    Duration = TimeSpan.Zero,
                    ErrorMessage = L.T(StringKey.VoiceNotRecording)
                };
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
                return new VoiceRecordingResult
                {
                    Success = false,
                    AudioData = audioData,
                    Duration = duration,
                    ErrorMessage = L.T(StringKey.VoiceRecordingDataEmpty)
                };
            }

            string? transcription = null;
            try
            {
                transcription = await TranscribeAsync(audioData, _options.WhisperLanguage, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, L.T(StringKey.VoiceTranscriptionFailed));
            }

            SetState(VoiceRecordingState.Idle);
            _logger?.LogInformation(L.T(StringKey.VoiceRecordingComplete, duration.TotalMilliseconds, transcription?.Length ?? 0));

            return new VoiceRecordingResult
            {
                Success = true,
                AudioData = audioData,
                Duration = duration,
                Transcription = transcription
            };
        }
        catch (Exception ex)
        {
            SetState(VoiceRecordingState.Error);
            return new VoiceRecordingResult
            {
                Success = false,
                AudioData = Array.Empty<byte>(),
                Duration = TimeSpan.Zero,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);

        return _options.Backend switch
        {
            SttBackend.WhisperApi => await TranscribeWithWhisperApiAsync(audioData, language, cancellationToken).ConfigureAwait(false),
            SttBackend.LocalModel => await TranscribeWithLocalModelAsync(audioData, language, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException(L.T(StringKey.VoiceUnsupportedSttBackend, _options.Backend))
        };
    }

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

    private async Task<string> TranscribeWithWhisperApiAsync(byte[] audioData, string? language, CancellationToken cancellationToken)
    {
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

        var response = await _httpClient.PostAsync(_options.WhisperApiEndpoint, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError(L.T(StringKey.VoiceWhisperApiFailed, response.StatusCode), responseBody);
            throw new InvalidOperationException(L.T(StringKey.VoiceWhisperApiCallFailed, response.StatusCode));
        }

        var result = JsonSerializer.Deserialize(responseBody, VoiceJsonContext.Default.WhisperTranscriptionResponse);
        return result?.Text ?? string.Empty;
    }

    private async Task<string> TranscribeWithLocalModelAsync(byte[] audioData, string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.LocalModelPath) || !_fs.FileExists(_options.LocalModelPath))
        {
            throw new InvalidOperationException(L.T(StringKey.VoiceLocalModelPathInvalid));
        }

        await Task.CompletedTask.ConfigureAwait(false);
        // P2-4: LocalModel STT 未实现，返回空字符串并记录警告，避免抛出 NotImplementedException 导致进程崩溃
        _logger?.LogWarning(L.T(StringKey.VoiceLocalSttNotImplemented));
        return string.Empty;
    }

    private async Task RecordLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (_recordingStream != null)
                    {
                        GenerateSilenceBuffer(buffer, _options.SampleRate);
                        await _recordingStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _stateLock.Release();
                }

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
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingStream?.Dispose();
        _stateLock.Dispose();
        _httpClient.Dispose();
    }

    async Task<JoinCode.Abstractions.Models.Voice.VoiceRecordingResult> JoinCode.Abstractions.Interfaces.IVoiceService.StopRecordingAsync(CancellationToken cancellationToken)
    {
        var r = await StopRecordingAsync(cancellationToken).ConfigureAwait(false);
        return new JoinCode.Abstractions.Models.Voice.VoiceRecordingResult
        {
            Success = r.Success,
            AudioData = r.AudioData,
            Duration = r.Duration,
            Transcription = r.Transcription,
            ErrorMessage = r.ErrorMessage,
            AudioFilePath = r.AudioFilePath
        };
    }

    bool JoinCode.Abstractions.Interfaces.IVoiceService.IsRecording => IsRecording;

    JoinCode.Abstractions.Models.Voice.VoiceRecordingState JoinCode.Abstractions.Interfaces.IVoiceService.State =>
        (JoinCode.Abstractions.Models.Voice.VoiceRecordingState)State;
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
