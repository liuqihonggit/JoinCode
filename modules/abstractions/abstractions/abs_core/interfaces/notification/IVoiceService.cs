
namespace JoinCode.Abstractions.Interfaces;

public interface IVoiceService {
    /// <summary>异步开始录音。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    /// <summary>异步停止录音并返回结果。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<VoiceRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default);
    /// <summary>异步转录音频文件。</summary>
    /// <param name="filePath">音频文件路径。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> TranscribeFileAsync(string filePath, string? language = null, CancellationToken cancellationToken = default);
    /// <summary>获取是否正在录音。</summary>
    bool IsRecording { get; }
    /// <summary>获取录音状态。</summary>
    VoiceRecordingState State { get; }
}