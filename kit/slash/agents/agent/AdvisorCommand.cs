
namespace JoinCode.ChatCommands;

/// <summary>
/// /advisor 命令 — 配置顾问模型，设置或关闭顾问审查模式
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Advisor, Description = "配置顾问模型", Usage = "/advisor [model|off]", Category = ChatCommandCategory.Agent, ArgumentHint = "[model|off]")]
[ChatCommandArg("model", Type = "string", Description = "顾问模型名称,或 off 关闭顾问模式", Enum = new[] { "off" })]
public sealed class AdvisorCommand(IModelConfigLoader? modelConfigLoader = null) : ChatCommandBase {
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;

    /// <summary>
    /// 执行 /advisor 命令，设置顾问模型或关闭顾问模式
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var advisorService = ChatCommandBase.GetService<IAdvisorService>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (advisorService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        if (string.IsNullOrEmpty(args)) {
            if (advisorService.IsAdvisorEnabled) {
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorModelLabel, advisorService.AdvisorModel));
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorReviewMsg));
            } else {
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorDisabled));
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorEnableHint));
            }
        } else if (args.Equals("off", StringComparison.OrdinalIgnoreCase) || args.Equals("unset", StringComparison.OrdinalIgnoreCase)) {
            advisorService.ClearAdvisorModel();
            TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorModeDisabled));
        } else {
            var allModelIds = _modelConfigLoader?.GetAllModelIds() ?? [];
            if (!allModelIds.Any(m => string.Equals(m, args, StringComparison.OrdinalIgnoreCase))) {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostAdvisorModelNotSupported, args)}{AnsiStyleEnumConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorSupportedModelsLabel));
                foreach (var model in allModelIds) {
                    TerminalHelper.WriteLine($"  {model}");
                }
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorConfirmSet));
            }

            advisorService.SetAdvisorModel(args);
            TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorModelLabel, args));
            TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorReviewMsg));
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }
}