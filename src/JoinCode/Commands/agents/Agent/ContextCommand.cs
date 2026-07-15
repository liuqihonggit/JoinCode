
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Context, Description = "显示当前会话上下文统计", Usage = "/context", Category = ChatCommandCategory.Info)]
public sealed class ContextCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Context;
    public string Description => "显示当前会话上下文统计";
    public string Usage => "/context";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var history = await context.Services.ChatService.GetMessageListAsync(context.CancellationToken);
        var sessionDuration = _clock.GetUtcNow() - context.SessionStartedAt;

        TerminalHelper.NewLine();

        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
        var currentModel = fastModeService?.PrimaryModelId ?? "unknown";
        var isFastMode = fastModeService?.IsFastModeActive ?? false;
        if (isFastMode && fastModeService?.FastModelId is not null)
        {
            currentModel = fastModeService.FastModelId;
        }

        var provider = context.Services.WorkflowConfig?.Provider?.Provider
            ?? Environment.GetEnvironmentVariable(JccEnvVar.Provider.ToValue())
            ?? ProviderKind.OpenAI.ToValue();
        var maxTokens = ResolveModelCatalog(context).GetModelsForProvider(provider)
            .FirstOrDefault(m => m.Id.Equals(currentModel, StringComparison.OrdinalIgnoreCase))?.ContextWindow
            ?? 128_000;

        var contextData = new ContextData
        {
            Model = currentModel,
            TotalTokens = EstimateTokens(history),
            MaxTokens = maxTokens,
        };

        var roleCounts = history
            .GroupBy(m => m.Role)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (role, count) in roleCounts)
        {
            var tokens = EstimateTokensForRole(role, count);
            contextData.Categories.Add(new ContextCategory(
                role.ToUpperInvariant(),
                tokens,
                role.ToLower() switch
                {
                    MessageRoleConstants.System => 0,
                    MessageRoleConstants.User => 1,
                    MessageRoleConstants.Assistant => 2,
                    MessageRoleConstants.Tool => 3,
                    _ => 4
                }
            ));
        }

        var visualizer = new ContextVisualizer();
        TerminalHelper.WriteLine(visualizer.Render(contextData));

        TerminalHelper.WriteLine($"  会话开始: {context.SessionStartedAt:yyyy-MM-dd HH:mm:ss} UTC");
        TerminalHelper.WriteLine($"  会话时长: {DurationFormatter.Format(sessionDuration, new DurationFormatOptions { UseAbbreviations = false, HideTrailingZeros = false })}");
        TerminalHelper.WriteLine($"  消息数量: {history.Count}");
        TerminalHelper.NewLine();

        return ChatCommandResult.Continue();
    }

    private static IModelCatalog ResolveModelCatalog(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("模型目录服务未初始化");
    }

    private static int EstimateTokens(IReadOnlyList<ApiMessageRecord> history)
    {
        var totalChars = 0;
        foreach (var msg in history)
            totalChars += msg.Content?.Length ?? 0;
        return totalChars / 4;
    }

    private static int EstimateTokensForRole(string role, int count)
    {
        var avgCharsPerMessage = role.ToLower() switch
        {
            MessageRoleConstants.System => 200,
            MessageRoleConstants.User => 150,
            MessageRoleConstants.Assistant => 500,
            MessageRoleConstants.Tool => 300,
            _ => 200
        };
        return count * avgCharsPerMessage / 4;
    }

}
