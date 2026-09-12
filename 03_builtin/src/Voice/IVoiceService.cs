
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

