
namespace Services.Voice;

public interface IVoiceService
{
    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default);
    Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken cancellationToken = default);
    Task<string> TranscribeFileAsync(string filePath, string? language = null, CancellationToken cancellationToken = default);
    bool IsRecording { get; }
    VoiceRecordingState State { get; }
    event EventHandler<VoiceRecordingState>? StateChanged;
}

public sealed class VoiceRecordingResult
{
    public required bool Success { get; init; }
    public required byte[] AudioData { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Transcription { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AudioFilePath { get; init; }
}
