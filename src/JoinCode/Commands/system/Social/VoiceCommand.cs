namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Voice, Description = "切换语音输入模式", Usage = "/voice [on|off|status]", Category = ChatCommandCategory.Social)]
public sealed class VoiceCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Voice;
    public override string Description => "切换语音输入模式";
    public override string Usage => "/voice [on|off|status]";
    public override bool IsHidden => true;
    protected override string ArgumentHintText => "[on|off|status]";

    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "start" or "record" => ToggleAction.On,
            "stop" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    protected override async Task OnEnabledAsync(ChatCommandContext context)
    {
        var voiceService = GetService<IVoiceService>(context);
        if (voiceService is null) return;

        try
        {
            await voiceService.StartRecordingAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("语音录制已开始，请说话...");
        }
        catch (Exception ex)
        {
            HandleError("启动语音录制", ex);
        }
    }

    protected override async Task OnDisabledAsync(ChatCommandContext context)
    {
        var voiceService = GetService<IVoiceService>(context);
        if (voiceService is null) return;

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
            HandleError("停止语音录制", ex);
        }
    }

    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var voiceService = GetService<IVoiceService>(context);
        if (voiceService is null) return Task.CompletedTask;

        var state = voiceService.State;
        var isRecording = voiceService.IsRecording;
        TerminalHelper.WriteLine($"语音服务状态: {state}");
        TerminalHelper.WriteLine($"录制中: {(isRecording ? "是" : "否")}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /voice on 开始录制");
        TerminalHelper.WriteLine("使用 /voice off 停止录制并识别");

        return Task.CompletedTask;
    }
}
