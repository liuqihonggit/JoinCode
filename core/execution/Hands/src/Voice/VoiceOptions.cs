
namespace Services.Voice;

[Register]
public sealed partial class VoiceOptions
{
    public SttBackend Backend { get; init; } = SttBackend.WhisperApi;
    public string WhisperApiEndpoint { get; init; } = "https://api.openai.com/v1/audio/transcriptions";
    public string? WhisperApiKey { get; init; }
    public string WhisperModel { get; init; } = "whisper-1";
    public string WhisperLanguage { get; init; } = "zh";
    public int SampleRate { get; init; } = 16000;
    public int Channels { get; init; } = 1;
    public TimeSpan MaxRecordingDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SilenceDetectionInterval { get; init; } = TimeSpan.FromSeconds(1);
    public double SilenceThreshold { get; init; } = 0.01;
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public string LocalModelPath { get; init; } = string.Empty;
}

public enum SttBackend
{
    [EnumValue("whisperApi")] WhisperApi,
    [EnumValue("localModel")] LocalModel
}
