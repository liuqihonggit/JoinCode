
namespace JoinCode.Abstractions.Models.Voice;

public enum VoiceRecordingState
{
    [EnumValue("idle")] Idle = 0,
    [EnumValue("recording")] Recording = 1,
    [EnumValue("processing")] Processing = 2,
    [EnumValue("error")] Error = 3
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
