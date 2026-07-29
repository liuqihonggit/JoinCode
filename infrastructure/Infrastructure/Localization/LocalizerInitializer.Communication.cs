namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterCommunicationEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === VoiceToolHandlers ===
        defaultEntries[StringKey.VoiceAlreadyRecording] = "Already recording, please stop the current recording first";
        defaultEntries[StringKey.VoiceRecordingStarted] = "Voice recording started";
        defaultEntries[StringKey.VoiceStartRecordingFailedLog] = "Failed to start voice recording";
        defaultEntries[StringKey.VoiceStartRecordingFailed] = "Failed to start voice recording: {0}";
        defaultEntries[StringKey.VoiceNotRecording] = "Not currently recording";
        defaultEntries[StringKey.VoiceRecordingStopped] = "Voice recording stopped";
        defaultEntries[StringKey.VoiceLabelDuration] = "Duration: {0}";
        defaultEntries[StringKey.VoiceLabelAudioSize] = "Audio size: {0} bytes";
        defaultEntries[StringKey.VoiceLabelTranscription] = "Transcription: {0}";
        defaultEntries[StringKey.VoiceLabelAudioFile] = "Audio file: {0}";
        defaultEntries[StringKey.VoiceLabelError] = "Error: {0}";
        defaultEntries[StringKey.VoiceStopRecordingFailedLog] = "Failed to stop voice recording";
        defaultEntries[StringKey.VoiceStopRecordingFailed] = "Failed to stop voice recording: {0}";
        defaultEntries[StringKey.VoiceFilePathCannotBeEmpty] = "File path cannot be empty";
        defaultEntries[StringKey.VoiceTranscriptionCompleted] = "Audio transcription completed";
        defaultEntries[StringKey.VoiceLabelFile] = "File: {0}";
        defaultEntries[StringKey.VoiceLabelLanguage] = "Language: {0}";
        defaultEntries[StringKey.VoiceTranscriptionFailedLog] = "Audio transcription failed: {0}";
        defaultEntries[StringKey.VoiceTranscriptionFailed] = "Audio transcription failed: {0}";
        defaultEntries[StringKey.VoiceServiceStatus] = "Voice service status";
        defaultEntries[StringKey.VoiceLabelState] = "State: {0}";
        defaultEntries[StringKey.VoiceLabelIsRecording] = "Recording: {0}";

        zhEntries[StringKey.VoiceAlreadyRecording] = "已经在录制中，请先停止当前录制";
        zhEntries[StringKey.VoiceRecordingStarted] = "语音录制已开始";
        zhEntries[StringKey.VoiceStartRecordingFailedLog] = "开始语音录制失败";
        zhEntries[StringKey.VoiceStartRecordingFailed] = "开始语音录制失败: {0}";
        zhEntries[StringKey.VoiceNotRecording] = "当前未在录制中";
        zhEntries[StringKey.VoiceRecordingStopped] = "语音录制已停止";
        zhEntries[StringKey.VoiceLabelDuration] = "录制时长: {0}";
        zhEntries[StringKey.VoiceLabelAudioSize] = "音频大小: {0} 字节";
        zhEntries[StringKey.VoiceLabelTranscription] = "转写结果: {0}";
        zhEntries[StringKey.VoiceLabelAudioFile] = "音频文件: {0}";
        zhEntries[StringKey.VoiceLabelError] = "错误: {0}";
        zhEntries[StringKey.VoiceStopRecordingFailedLog] = "停止语音录制失败";
        zhEntries[StringKey.VoiceStopRecordingFailed] = "停止语音录制失败: {0}";
        zhEntries[StringKey.VoiceFilePathCannotBeEmpty] = "文件路径不能为空";
        zhEntries[StringKey.VoiceTranscriptionCompleted] = "音频转写完成";
        zhEntries[StringKey.VoiceLabelFile] = "文件: {0}";
        zhEntries[StringKey.VoiceLabelLanguage] = "语言: {0}";
        zhEntries[StringKey.VoiceTranscriptionFailedLog] = "音频转写失败: {0}";
        zhEntries[StringKey.VoiceTranscriptionFailed] = "音频转写失败: {0}";
        zhEntries[StringKey.VoiceServiceStatus] = "语音服务状态";
        zhEntries[StringKey.VoiceLabelState] = "状态: {0}";
        zhEntries[StringKey.VoiceLabelIsRecording] = "正在录制: {0}";

        // === SendUserFileToolHandlers ===
        defaultEntries[StringKey.SendUserFilePathCannotBeEmpty] = "file_path cannot be empty";
        defaultEntries[StringKey.SendUserFileNotFound] = "File not found: {0}";
        defaultEntries[StringKey.SendUserFileSent] = "File sent";
        defaultEntries[StringKey.SendUserFileLabelPath] = "Path: {0}";
        defaultEntries[StringKey.SendUserFileLabelSize] = "Size: {0}";
        defaultEntries[StringKey.SendUserFileLabelModifiedTime] = "Modified: {0}";
        defaultEntries[StringKey.SendUserFileLabelDescription] = "Description: {0}";
        defaultEntries[StringKey.SendUserFileDownloadLinkFailedLog] = "Failed to generate download link";
        defaultEntries[StringKey.SendUserFileDownloadLinkFailed] = "Failed to generate download link";
        defaultEntries[StringKey.SendUserFileContentPreview] = "--- File Content Preview ---";
        defaultEntries[StringKey.SendUserFilePreviewLineCount] = "... ({0} lines total)";
        defaultEntries[StringKey.SendUserFilePreviewEnd] = "--- Preview End ---";
        defaultEntries[StringKey.SendUserFileFailedLog] = "Failed to send file: {0}";
        defaultEntries[StringKey.SendUserFileFailed] = "Failed to send file: {0}";

        zhEntries[StringKey.SendUserFilePathCannotBeEmpty] = "file_path 不能为空";
        zhEntries[StringKey.SendUserFileNotFound] = "文件不存在: {0}";
        zhEntries[StringKey.SendUserFileSent] = "文件已发送";
        zhEntries[StringKey.SendUserFileLabelPath] = "路径: {0}";
        zhEntries[StringKey.SendUserFileLabelSize] = "大小: {0}";
        zhEntries[StringKey.SendUserFileLabelModifiedTime] = "修改时间: {0}";
        zhEntries[StringKey.SendUserFileLabelDescription] = "说明: {0}";
        zhEntries[StringKey.SendUserFileDownloadLinkFailedLog] = "生成下载链接失败";
        zhEntries[StringKey.SendUserFileDownloadLinkFailed] = "下载链接生成失败";
        zhEntries[StringKey.SendUserFileContentPreview] = "--- 文件内容预览 ---";
        zhEntries[StringKey.SendUserFilePreviewLineCount] = "... (共 {0} 行)";
        zhEntries[StringKey.SendUserFilePreviewEnd] = "--- 预览结束 ---";
        zhEntries[StringKey.SendUserFileFailedLog] = "发送文件失败: {0}";
        zhEntries[StringKey.SendUserFileFailed] = "发送文件失败: {0}";

        // === RemoteTriggerToolHandlers ===
        defaultEntries[StringKey.RemoteTriggerServiceNotConfigured] = "Remote trigger service not configured. Please set JCC_ENDPOINT and JCC_API_KEY environment variables.";
        defaultEntries[StringKey.RemoteTriggerUnknownAction] = "Unknown action: {0}, supported: list/get/create/update/run";
        defaultEntries[StringKey.RemoteTriggerActionRequiresId] = "{0} action requires trigger_id";
        defaultEntries[StringKey.RemoteTriggerHeader] = "Remote Trigger - {0}";
        defaultEntries[StringKey.RemoteTriggerLabelStatusCode] = "Status code: {0}";
        defaultEntries[StringKey.RemoteTriggerErrorResponse] = "Error response: {0}";
        defaultEntries[StringKey.RemoteTriggerFailedLog] = "Remote trigger operation failed";
        defaultEntries[StringKey.RemoteTriggerFailed] = "Remote trigger operation failed: {0}";

        zhEntries[StringKey.RemoteTriggerServiceNotConfigured] = "远程触发器服务未配置。请设置 JCC_ENDPOINT 和 JCC_API_KEY 环境变量。";
        zhEntries[StringKey.RemoteTriggerUnknownAction] = "未知操作: {0}，支持: list/get/create/update/run";
        zhEntries[StringKey.RemoteTriggerActionRequiresId] = "{0} 操作需要指定 trigger_id";
        zhEntries[StringKey.RemoteTriggerHeader] = "远程触发器 - {0}";
        zhEntries[StringKey.RemoteTriggerLabelStatusCode] = "状态码: {0}";
        zhEntries[StringKey.RemoteTriggerErrorResponse] = "错误响应: {0}";
        zhEntries[StringKey.RemoteTriggerFailedLog] = "远程触发器操作失败";
        zhEntries[StringKey.RemoteTriggerFailed] = "远程触发器操作失败: {0}";

        // === PushNotificationToolHandlers ===
        defaultEntries[StringKey.PushNotificationTitleCannotBeEmpty] = "title cannot be empty";
        defaultEntries[StringKey.PushNotificationMessageCannotBeEmpty] = "message cannot be empty";
        defaultEntries[StringKey.PushNotificationSentViaServiceLog] = "Push notification sent via NotificationService: {0}";
        defaultEntries[StringKey.PushNotificationSent] = "Notification sent";
        defaultEntries[StringKey.PushNotificationLabelTitle] = "Title: {0}";
        defaultEntries[StringKey.PushNotificationLabelMessage] = "Message: {0}";
        defaultEntries[StringKey.PushNotificationLabelLevel] = "Level: {0}";
        defaultEntries[StringKey.PushNotificationFailedLog] = "Push notification failed";
        defaultEntries[StringKey.PushNotificationFailed] = "Push notification failed: {0}";

        zhEntries[StringKey.PushNotificationTitleCannotBeEmpty] = "title 不能为空";
        zhEntries[StringKey.PushNotificationMessageCannotBeEmpty] = "message 不能为空";
        zhEntries[StringKey.PushNotificationSentViaServiceLog] = "推送通知已通过 NotificationService 发送: {0}";
        zhEntries[StringKey.PushNotificationSent] = "通知已发送";
        zhEntries[StringKey.PushNotificationLabelTitle] = "标题: {0}";
        zhEntries[StringKey.PushNotificationLabelMessage] = "内容: {0}";
        zhEntries[StringKey.PushNotificationLabelLevel] = "级别: {0}";
        zhEntries[StringKey.PushNotificationFailedLog] = "推送通知失败";
        zhEntries[StringKey.PushNotificationFailed] = "推送通知失败: {0}";
    }
}
