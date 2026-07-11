namespace Core.Tests.Services.Voice;

public sealed class VoiceServiceTests : IDisposable
{
    private readonly VoiceService _service;
    private readonly VoiceOptions _options;

    public VoiceServiceTests()
    {
        _options = new VoiceOptions
        {
            Backend = SttBackend.WhisperApi,
            WhisperApiKey = TestConfiguration.GetRealApiKey()
        };
        var mockProvider = new Infrastructure.Http.MockHttpClientProvider();
        mockProvider.SetupDefaultResponse(System.Net.HttpStatusCode.OK, """{"text":"mock transcription"}""");
        _service = new VoiceService(_options, new IO.FileSystem.PhysicalFileSystem(), mockProvider);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task StartRecordingAsync_WhenIdle_ChangesStateToRecording()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        _service.State.Should().Be(VoiceRecordingState.Idle);

        try
        {
            await _service.StartRecordingAsync(ct).ConfigureAwait(true);

            _service.State.Should().Be(VoiceRecordingState.Recording);
            _service.IsRecording.Should().BeTrue();
        }
        finally
        {
            if (_service.IsRecording)
            {
                await _service.StopRecordingAsync(ct).ConfigureAwait(true);
            }
        }
    }

    [Fact]
    public async Task StopRecordingAsync_WhenRecording_ReturnsTranscribedText()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        await _service.StartRecordingAsync(ct).ConfigureAwait(true);

        var result = await _service.StopRecordingAsync(ct).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task StopRecordingAsync_WhenIdle_ReturnsFailure()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        var result = await _service.StopRecordingAsync(ct).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartRecordingAsync_WhenAlreadyRecording_DoesNotChangeState()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        try
        {
            await _service.StartRecordingAsync(ct).ConfigureAwait(true);
            var stateBefore = _service.State;

            await _service.StartRecordingAsync(ct).ConfigureAwait(true);

            _service.State.Should().Be(stateBefore);
        }
        finally
        {
            if (_service.IsRecording)
            {
                await _service.StopRecordingAsync(ct).ConfigureAwait(true);
            }
        }
    }

    [Fact]
    public async Task StateChanged_EventRaisedOnStateChange()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        var stateChanges = new List<VoiceRecordingState>();
        _service.StateChanged += (_, state) => stateChanges.Add(state);

        try
        {
            await _service.StartRecordingAsync(ct).ConfigureAwait(true);

            stateChanges.Should().Contain(VoiceRecordingState.Recording);
        }
        finally
        {
            if (_service.IsRecording)
            {
                await _service.StopRecordingAsync(ct).ConfigureAwait(true);
            }
        }
    }

    [Fact]
    public async Task TranscribeFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = () => _service.TranscribeFileAsync("/nonexistent/file.wav", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<FileNotFoundException>().ConfigureAwait(true);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var mockProvider = new Infrastructure.Http.MockHttpClientProvider();
        var act = () => new VoiceService(null!, new IO.FileSystem.PhysicalFileSystem(), mockProvider);

        act.Should().Throw<ArgumentNullException>();
    }
}
