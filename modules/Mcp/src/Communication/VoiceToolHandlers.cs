

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Voice, Optional = true)]
public sealed partial class VoiceToolHandlers
{
    private readonly IVoiceService _voiceService;
    [Inject] private readonly ILogger<VoiceToolHandlers>? _logger;

    public VoiceToolHandlers(IVoiceService voiceService, ILogger<VoiceToolHandlers>? logger = null)
    {
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.VoiceStartRecording, "Start voice recording", "voice")]
    public async Task<ToolResult> VoiceStartRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_voiceService.IsRecording)
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.VoiceAlreadyRecording))
                    .Build();
            }

            await _voiceService.StartRecordingAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Voice recording started");

            return McpResultBuilder.Success()
                .WithText(L.T(StringKey.VoiceRecordingStarted))
                .Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceStartRecordingFailedLog));
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceStartRecordingFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(SystemToolNameConstants.VoiceStopRecording, "Stop voice recording and return result", "voice")]
    public async Task<ToolResult> VoiceStopRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_voiceService.IsRecording)
            {
                return McpResultBuilder.Error()
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
                return McpResultBuilder.Error().WithText(response.ToString()).Build();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceStopRecordingFailedLog));
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceStopRecordingFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(SystemToolNameConstants.VoiceTranscribe, "Transcribe audio file", "voice")]
    public async Task<ToolResult> VoiceTranscribeAsync(
        [McpToolParameter("Audio file path")] string file_path,
        [McpToolParameter("Language code (optional, e.g. zh/en)", Required = false)] string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VoiceFilePathCannotBeEmpty)).Build();
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

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.VoiceTranscriptionFailedLog, file_path));
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VoiceTranscriptionFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(SystemToolNameConstants.VoiceStatus, "Get voice service status", "voice")]
    public Task<ToolResult> VoiceStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder(128);
        response.AppendLine(L.T(StringKey.VoiceServiceStatus));
        response.AppendLine(L.T(StringKey.VoiceLabelState, _voiceService.State.ToString()));
        response.AppendLine(L.T(StringKey.VoiceLabelIsRecording, _voiceService.IsRecording.ToString()));

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }
}
