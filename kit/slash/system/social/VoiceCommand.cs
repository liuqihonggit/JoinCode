namespace JoinCode.ChatCommands;

/// <summary>
/// /voice 命令 — 切换语音输入模式
/// 通过 IVoiceService 启动/停止语音录制并识别为文本
/// 支持 on/off/status/start/stop/record 多种操作别名
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Voice, Description = "切换语音输入模式", Usage = "/voice [on|off|status]", Category = ChatCommandCategory.Social)]
[ChatCommandArg("action", Type = "string", Description = "语音操作", Enum = new[] { "on", "off", "status", "start", "stop", "record" })]
public sealed class VoiceCommand : ToggleCommandBase {
    /// <summary>
    /// 获取命令名称
    /// </summary>
    public override string Name => ChatCommandNameEnumConstants.Voice;
    /// <summary>
    /// 获取命令描述
    /// </summary>
    public override string Description => "切换语音输入模式";
    /// <summary>
    /// 获取命令用法
    /// </summary>
    public override string Usage => "/voice [on|off|status]";
    /// <summary>
    /// 获取是否隐藏命令
    /// </summary>
    public override bool IsHidden => true;
    /// <summary>
    /// 获取参数提示文本
    /// </summary>
    protected override string ArgumentHintText => "[on|off|status]";

    /// <summary>
    /// 将参数解析为切换动作 — start/record 映射为开，stop 映射为关，其余委托给 FromValue
    /// </summary>
    /// <param name="args">原始参数字符串</param>
    /// <returns>解析得到的切换动作，无法识别时返回 null</returns>
    protected override ToggleAction? ResolveToggleAction(string args) {
        var lower = args.ToLowerInvariant();
        return lower switch {
            "start" or "record" => ToggleAction.On,
            "stop" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    /// <summary>
    /// 获取无参数时的默认动作 — 语音命令无参数时显示状态
    /// </summary>
    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    /// <summary>
    /// 启用语音录制 — 调用 IVoiceService.StartRecordingAsync 开始录音
    /// </summary>
    /// <param name="context">命令执行上下文，包含取消令牌</param>
    protected override async Task OnEnabledAsync(ChatCommandContext context) {
        var voiceService = GetService<IVoiceService>(context);
        if (voiceService is null) return;

        try {
            await voiceService.StartRecordingAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("语音录制已开始，请说话...");
        } catch (Exception ex) {
            HandleError("启动语音录制", ex);
        }
    }

    /// <summary>
    /// 停止语音录制 — 调用 IVoiceService.StopRecordingAsync 停止录音并输出识别结果
    /// </summary>
    /// <param name="context">命令执行上下文，包含取消令牌</param>
    protected override async Task OnDisabledAsync(ChatCommandContext context) {
        var voiceService = GetService<IVoiceService>(context);
        if (voiceService is null) return;

        try {
            var result = await voiceService.StopRecordingAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("语音录制已停止");
            if (!string.IsNullOrEmpty(result.Transcription)) {
                TerminalHelper.WriteLine($"识别结果: {result.Transcription}");
            }
        } catch (Exception ex) {
            HandleError("停止语音录制", ex);
        }
    }

    /// <summary>
    /// 打印语音服务当前状态 — 输出服务状态、录制状态及使用提示
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected override Task PrintStatusAsync(ChatCommandContext context) {
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