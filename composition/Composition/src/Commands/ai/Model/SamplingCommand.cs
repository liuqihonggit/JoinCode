namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Sampling, Description = "查看或设置采样参数（温度/最大Token）", Usage = "/sampling [温度] [最大Token|unset]", Category = ChatCommandCategory.Model, ArgumentHint = "[温度 0-2] [最大Token]|unset")]
public sealed class SamplingCommand : ChatCommandBase
{
    /// <summary>温度合法上界 — 主流 LLM API 约定 0-2</summary>
    private const float MaxTemperature = 2f;

    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var settingsProvider = context.GetCommandServices().ExecutionSettingsProvider;
        if (settingsProvider is null)
        {
            ChatCommandBase.HandleError("采样参数", new InvalidOperationException("引擎未就绪：缺少 ExecutionSettingsProvider"));
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var args = ChatCommandBase.GetSplitArgs(context);

        // 无参 → 查询模式：显示当前值（对齐 /effort current）
        if (args.Length == 0)
        {
            var tempText = settingsProvider.Temperature?.ToString("0.00") ?? "默认";
            var maxText = settingsProvider.MaxTokens?.ToString() ?? "默认";
            TerminalHelper.WriteLine($"采样参数: 温度 {tempText}, 最大 {maxText} tokens");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        // unset → 清除覆盖，回退引擎默认
        if (args[0].Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            settingsProvider.Temperature = null;
            settingsProvider.MaxTokens = null;
            TerminalHelper.WriteLine("采样参数已重置为引擎默认值");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        if (!float.TryParse(args[0], out var temperature) || temperature is < 0f or > MaxTemperature)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}无效温度: {args[0]}。有效范围 0-{MaxTemperature.ToString("0")}，或不带参数查询当前值{AnsiStyleConstants.Reset}");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        settingsProvider.Temperature = temperature;

        // 第二个可选参数 = MaxTokens；只给温度时保持原值（对齐 GUI 只动对应滑块语义）
        if (args.Length >= 2)
        {
            if (!int.TryParse(args[1], out var maxTokens) || maxTokens <= 0)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}无效最大 Token 数: {args[1]}。须为正整数{AnsiStyleConstants.Reset}");
                return Task.FromResult(ChatCommandResult.Continue());
            }
            settingsProvider.MaxTokens = maxTokens;
        }

        var newTemp = settingsProvider.Temperature?.ToString("0.00") ?? "默认";
        var newMax = settingsProvider.MaxTokens?.ToString() ?? "默认";
        TerminalHelper.WriteLine($"采样参数已更新: 温度 {newTemp}, 最大 {newMax} tokens");
        return Task.FromResult(ChatCommandResult.Continue());
    }
}
