

namespace McpToolDispatch;

/// <summary>
/// 语音工具处理器 — 提供语音录制、停止、转写、状态查询功能
/// </summary>
[McpToolDispatch(ToolCategory.Voice, Optional = true)]
public sealed partial class VoiceToolHandlers
{
    private readonly IVoiceService _voiceService;
    private readonly ILogger<VoiceToolHandlers>? _logger;

    /// <summary>
    /// 初始化语音工具处理器
    /// </summary>
    /// <param name="voiceService">语音服务实例</param>
    /// <param name="logger">日志记录器（可选）</param>
    public VoiceToolHandlers(IVoiceService voiceService, ILogger<VoiceToolHandlers>? logger = null)
    {
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
        _logger = logger;
    }

    /// <summary>
    /// 检测是否为 CLI 单次调用模式（jcc mcp_call）— 录制状态不跨进程持久化
    /// </summary>
    private static bool IsCliSingleCallMode
        => Array.IndexOf(Environment.GetCommandLineArgs(), "mcp_call") >= 0;

    /// <summary>
    /// 启动语音录制 — 需在交互式会话（jcc chat）中使用，CLI 单次调用模式下录制状态不跨进程持久化
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameConstants.VoiceStartRecording, "Start voice recording", "voice")]
    public async Task<ToolResult> VoiceStartRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsCliSingleCallMode)
            {
                return ToolResultBuilder.Error()
                    .WithText("语音录制需要在交互式会话中使用（jcc chat），CLI 单次调用（jcc mcp_call）模式下录制状态不跨进程持久化")
                    .Build();
            }

            if (_voiceService.IsRecording)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.VoiceAlreadyRecording))
                    .Build();
            }

            await _voiceService.StartRecordingAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Voice recording started");

            return ToolResultBuilder.Success()
                .WithText(L.T(StringKey.VoiceRecordingStarted))
                .Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceStartRecordingFailedLog));
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceStartRecordingFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 停止语音录制并返回录制结果（时长、音频大小、转写文本、音频文件路径）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含录制结果的工具执行结果</returns>
    [McpTool(SystemToolNameConstants.VoiceStopRecording, "Stop voice recording and return result", "voice")]
    public async Task<ToolResult> VoiceStopRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_voiceService.IsRecording)
            {
                return ToolResultBuilder.Success()
                    .WithText(L.T(StringKey.VoiceNotRecording))
                    .Build();
            }

            var result = await _voiceService.StopRecordingAsync(cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.VoiceRecordingStopped));
            response.AppendLine(L.T(StringKey.VoiceLabelDuration, result.Duration.ToString(@"hh\:mm\:ss")));
            response.AppendLine(L.T(StringKey.VoiceLabelAudioSize, result.AudioData.Length.ToString()));

            if (!string.IsNullOrEmpty(result.Transcription))
            {
                response.AppendLine(L.T(StringKey.VoiceLabelTranscription, result.Transcription));
            }

            if (!string.IsNullOrEmpty(result.AudioFilePath))
            {
                response.AppendLine(L.T(StringKey.VoiceLabelAudioFile, result.AudioFilePath));
            }

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                response.AppendLine(L.T(StringKey.VoiceLabelError, result.ErrorMessage));
                return ToolResultBuilder.Error().WithText(response.ToString()).Build();
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceStopRecordingFailedLog));
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceStopRecordingFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 转写音频文件为文本
    /// </summary>
    /// <param name="file_path">音频文件路径</param>
    /// <param name="language">语言代码（可选，如 zh/en）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含转写文本的工具执行结果</returns>
    [McpTool(SystemToolNameConstants.VoiceTranscribe, "Transcribe audio file", "voice")]
    public async Task<ToolResult> VoiceTranscribeAsync(
        [McpToolParameter("Audio file path")] string file_path,
        [McpToolParameter("Language code (optional, e.g. zh/en)", Required = false)] string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.VoiceFilePathCannotBeEmpty)).Build();
        }

        try
        {
            var transcription = await _voiceService.TranscribeFileAsync(file_path, language, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.VoiceTranscriptionCompleted));
            response.AppendLine(L.T(StringKey.VoiceLabelFile, file_path));

            if (!string.IsNullOrEmpty(language))
            {
                response.AppendLine(L.T(StringKey.VoiceLabelLanguage, language));
            }

            response.AppendLine(L.T(StringKey.VoiceLabelTranscription, transcription));

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceTranscriptionFailedLog, file_path));
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceTranscriptionFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 查询语音服务当前状态（状态、是否正在录制）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含语音服务状态的工具执行结果</returns>
    [McpTool(SystemToolNameConstants.VoiceStatus, "Get voice service status", "voice")]
    public Task<ToolResult> VoiceStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder(128);
        response.AppendLine(L.T(StringKey.VoiceServiceStatus));
        response.AppendLine(L.T(StringKey.VoiceLabelState, _voiceService.State.ToString()));
        response.AppendLine(L.T(StringKey.VoiceLabelIsRecording, _voiceService.IsRecording.ToString()));

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }
}
