
namespace JoinCode.ChatCommands;

/// <summary>
/// /voice 命令 — 对齐 TS voice.ts
/// TS 使用 Web Speech API / Whisper STT，C# 使用 IVoiceService
/// 对齐内容：on+off+status 核心操作
/// 架构差异：TS 有实时音频流+React 波形可视化，C# 为录制-识别模式
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Voice, Description = "切换语音输入模式", Usage = "/voice [on|off|status]", Category = ChatCommandCategory.Social)]
public sealed class VoiceCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Voice;
    public string Description => "切换语音输入模式";
    public string Usage => "/voice [on|off|status]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off|status]";
    public bool IsHidden => true;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var voiceService = ChatCommandBase.GetService<IVoiceService>(context);

        if (voiceService is null)
            return ChatCommandResult.Continue();

        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        // ToggleAction 枚举标准映射 (on/off/toggle/status),start/stop/record 走别名映射
        var toggle = args switch
        {
            "start" or "record" => ToggleAction.On,
            "stop" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };

        switch (toggle)
        {
            case ToggleAction.On:
                try
                {
                    await voiceService.StartRecordingAsync(context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine("语音录制已开始，请说话...");
                }
                catch (Exception ex)
                {
                    ChatCommandBase.HandleError("启动语音录制", ex);
                }
                break;
            case ToggleAction.Off:
                try
                {
                    var result = await voiceService.StopRecordingAsync(context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine("语音录制已停止");
                    if (!string.IsNullOrEmpty(result.Transcription))
                    {
                        TerminalHelper.WriteLine($"识别结果: {result.Transcription}");
                    }
                }
                catch (Exception ex)
                {
                    ChatCommandBase.HandleError("停止语音录制", ex);
                }
                break;
            default:
                var state = voiceService.State;
                var isRecording = voiceService.IsRecording;
                TerminalHelper.WriteLine($"语音服务状态: {state}");
                TerminalHelper.WriteLine($"录制中: {(isRecording ? "是" : "否")}");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("使用 /voice on 开始录制");
                TerminalHelper.WriteLine("使用 /voice off 停止录制并识别");
                break;
        }

        return ChatCommandResult.Continue();
    }
}
