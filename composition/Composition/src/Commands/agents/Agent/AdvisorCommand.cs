
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Advisor, Description = "配置顾问模型", Usage = "/advisor [model|off]", Category = ChatCommandCategory.Agent, ArgumentHint = "[model|off]")]
public sealed class AdvisorCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var advisorService = ChatCommandBase.GetService<IAdvisorService>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (advisorService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        if (string.IsNullOrEmpty(args))
        {
            if (advisorService.IsAdvisorEnabled)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorModelLabel, advisorService.AdvisorModel));
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorReviewMsg));
            }
            else
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorDisabled));
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorEnableHint));
            }
        }
        else if (args.Equals("off", StringComparison.OrdinalIgnoreCase) || args.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            advisorService.ClearAdvisorModel();
            TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorModeDisabled));
        }
        else
        {
            var allModelIds = ModelConfigLoader.GetAllModelIds();
            if (!allModelIds.Any(m => string.Equals(m, args, StringComparison.OrdinalIgnoreCase)))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostAdvisorModelNotSupported, args)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorSupportedModelsLabel));
                foreach (var model in allModelIds)
                {
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
