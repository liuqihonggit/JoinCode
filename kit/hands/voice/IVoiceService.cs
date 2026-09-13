
namespace Services.Voice;

/// <summary>
/// 语音服务接口 — 提供音频录制、停止、转录等能力。
/// </summary>
public interface IVoiceService
{
    /// <summary>
    /// 异步开始录制音频。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task StartRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步停止录制并返回录制结果。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含音频数据、时长及转录文本的录制结果。</returns>
    Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步转录音频字节数据为文本。
    /// </summary>
    /// <param name="audioData">音频字节数据。</param>
    /// <param name="language">可选的语言代码，为 null 时使用配置默认值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>转录得到的文本。</returns>
    Task<string> TranscribeAsync(byte[] audioData, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步转录指定音频文件为文本。
    /// </summary>
    /// <param name="filePath">音频文件路径。</param>
    /// <param name="language">可选的语言代码，为 null 时使用配置默认值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>转录得到的文本。</returns>
    Task<string> TranscribeFileAsync(string filePath, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前是否正在录制音频。
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// 获取当前语音录制状态。
    /// </summary>
    VoiceRecordingState State { get; }

    /// <summary>
    /// 录制状态变更事件 — 状态切换时触发，参数为新的状态值。
    /// </summary>
    event EventHandler<VoiceRecordingState>? StateChanged;
}

