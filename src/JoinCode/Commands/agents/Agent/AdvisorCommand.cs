
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Advisor, Description = "配置顾问模型", Usage = "/advisor [model|off]", Category = ChatCommandCategory.Agent)]
public sealed class AdvisorCommand : IChatCommand
{
    private static readonly FrozenSet<string> SupportedAdvisorModels = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "claude-3-opus", "claude-3-sonnet", "claude-3.5-sonnet", "claude-3.5-haiku",
        "gpt-4o", "gpt-4-turbo", "o1", "o1-mini", "o3", "o3-mini",
        "deepseek-chat", "deepseek-reasoner");

    public string Name => ChatCommandNameConstants.Advisor;
    public string Description => "配置顾问模型";
    public string Usage => "/advisor [model|off]";
    public string[] Aliases => [];
    public string ArgumentHint => "[model|off]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
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
            if (!IsModelSupported(args))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostAdvisorModelNotSupported, args)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.HostAdvisorSupportedModelsLabel));
                foreach (var model in SupportedAdvisorModels)
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

    private static bool IsModelSupported(string modelId)
    {
        foreach (var supported in SupportedAdvisorModels)
        {
            if (modelId.Contains(supported, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
