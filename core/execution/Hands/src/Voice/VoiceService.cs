
namespace Services.Voice;

[Register(typeof(IVoiceService), ServiceLifetime.Singleton)]
[Register(typeof(JoinCode.Abstractions.Interfaces.IVoiceService), ServiceLifetime.Singleton)]
public sealed partial class VoiceService : ServiceEntity, IVoiceService, JoinCode.Abstractions.Interfaces.IVoiceService, IDisposable
{
    private readonly VoiceOptions _options;
    private readonly IResilientHttpClientProvider _resilientProvider;
    private readonly ILogger<VoiceService>? _logger;
    private readonly IClockService _clock;
    private readonly IFileSystem _fs;
    private readonly AsyncLock _stateLock = new();

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
        IResilientHttpClientProvider resilientProvider,
        ILogger<VoiceService>? logger = null,
        IClockService? clock = null)
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

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _stateLock.LockAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _stateLock.LockAsync(cancellationToken).ConfigureAwait(false);
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

    }

    public async Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);

        return _options.Backend switch
        {
            SttBackend.WhisperApi => await TranscribeWithWhisperApiAsync(audioData, language, cancellationToken).ConfigureAwait(false),
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
                using var guard = await _stateLock.LockAsync(cancellationToken).ConfigureAwait(false);

                if (_recordingStream != null)
                {
                    GenerateSilenceBuffer(buffer, _options.SampleRate);
                    await _recordingStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
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

    protected override void OnDispose()
    {
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingStream?.Dispose();
        _stateLock.Dispose();
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
