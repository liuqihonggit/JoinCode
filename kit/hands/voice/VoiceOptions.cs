
namespace Services.Voice;

/// <summary>
/// 语音服务配置选项 — 描述 Whisper API 端点、模型、采样率、静音检测等参数。
/// </summary>
[Register(typeof(VoiceOptions), ServiceLifetime.Singleton)]
public sealed partial class VoiceOptions : ServiceEntity
{
    /// <summary>
    /// 语音转文本后端类型，默认 WhisperApi。
    /// </summary>
    public SttBackend Backend { get; init; } = SttBackend.WhisperApi;

    /// <summary>
    /// Whisper API 端点地址。
    /// </summary>
    public string WhisperApiEndpoint { get; init; } = "https://api.openai.com/v1/audio/transcriptions";

    /// <summary>
    /// Whisper API 密钥，为 null 表示未配置。
    /// </summary>
    public string? WhisperApiKey { get; init; }

    /// <summary>
    /// Whisper 模型名称，默认 "whisper-1"。
    /// </summary>
    public string WhisperModel { get; init; } = "whisper-1";

    /// <summary>
    /// 转录语言代码，默认 "zh"。
    /// </summary>
    public string WhisperLanguage { get; init; } = "zh";

    /// <summary>
    /// 音频采样率（Hz），默认 16000。
    /// </summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>
    /// 音频声道数，默认 1（单声道）。
    /// </summary>
    public int Channels { get; init; } = 1;

    /// <summary>
    /// 最大录制时长，默认 5 分钟。
    /// </summary>
    public TimeSpan MaxRecordingDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 静音检测间隔，默认 1 秒。
    /// </summary>
    public TimeSpan SilenceDetectionInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 静音判定阈值，默认 0.01。
    /// </summary>
    public double SilenceThreshold { get; init; } = 0.01;

    /// <summary>
    /// 静音超时时长，达到则停止录制，默认 3 秒。
    /// </summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// 语音转文本（STT）后端类型枚举。
/// </summary>
public enum SttBackend
{
    /// <summary>
    /// OpenAI Whisper HTTP API 后端。
    /// </summary>
    [EnumValue("whisperApi")] WhisperApi
}
