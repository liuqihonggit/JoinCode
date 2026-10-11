
namespace JoinCode.Abstractions.Models.Voice;

public enum VoiceRecordingState {
    [EnumValue("idle")] Idle = 0,
    [EnumValue("recording")] Recording = 1,
    [EnumValue("processing")] Processing = 2,
    [EnumValue("error")] Error = 3
}

public sealed class VoiceRecordingResult {
    /// <summary>获取是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取音频数据。</summary>
    public required byte[] AudioData { get; init; }
    /// <summary>获取录音时长。</summary>
    public required TimeSpan Duration { get; init; }
    /// <summary>获取转录文本。</summary>
    public string? Transcription { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取音频文件路径。</summary>
    public string? AudioFilePath { get; init; }
}