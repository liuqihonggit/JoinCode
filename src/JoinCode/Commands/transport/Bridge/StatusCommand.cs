namespace JoinCode.ChatCommands;

/// <summary>RenderOverview 渲染上下文 — 封装 12 个参数，消除 JCC1006 违规</summary>
internal record class RenderOverviewContext(
    string Version, string Cwd, string SessionId, DateTime StartedAt, TimeSpan Duration,
    string ProviderName, string CurrentModel, bool IsFastMode, string EffortLevel,
    string ApiStatus, string McpStatus, string MessageInfo);

[ChatCommand(Name = ChatCommandNameConstants.Status, Description = "显示版本、模型、账户、API连接和工具状态", Usage = "/status", Category = ChatCommandCategory.Info)]
public sealed class StatusCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Status;
    public string Description => "显示版本、模型、账户、API连接和工具状态";
    public string Usage => "/status";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 预收集数据
        var version = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
        var cwd = context.Services.FileSystem.GetCurrentDirectory();
        var duration = _clock.GetUtcNow() - context.SessionStartedAt;

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
        var providerName = ResolveModelCatalog(context).GetProviderDisplayName(provider);

        // 多态：通过 IProviderDefinition.ResolveApiKeyFromEnv 消除 ProviderKind switch
        // 优先级：JccEnvVar.ApiKey 全局 > Provider 专属 env var（由各 ProviderDefinition 自己决定）
        var providerDefinition = ResolveProviderDefinition(context, provider);
        var apiKey = Environment.GetEnvironmentVariable(JccEnvVar.ApiKey.ToValue())
            ?? providerDefinition?.ResolveApiKeyFromEnv();

        var executionSettings = context.Services.ExecutionSettingsProvider;
        var effortLevel = executionSettings?.EffortLevel.ToValue() ?? "";

        var services = context.Services ?? throw new InvalidOperationException("Services not available.");
        var hasMcpTools = services.ToolRegistry is not null;

        // 获取消息历史
        string messageInfo;
        try
        {
            var history = await services.ChatService.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
            var userCount = history.Count(m =>
                string.Equals(m.Role, MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase));
            var assistantCount = history.Count(m =>
                string.Equals(m.Role, MessageRoleConstants.Assistant, StringComparison.OrdinalIgnoreCase));
            var lastMsg = history.Count > 0
                ? $"{history[^1].Timestamp:HH:mm:ss} ({history[^1].Role})"
                : "无";

            messageInfo = $"  总消息数:   {history.Count}\n  用户输入:   {userCount}\n  AI回复:     {assistantCount}\n  最后活动:   {lastMsg}";
        }
        catch
        {
            messageInfo = $"  {TerminalColors.Muted}无法获取对话历史{AnsiStyleConstants.Reset}";
        }

        // 获取 Token 用量
        string tokenInfo;
        if (services.UsageTracker is not null)
        {
            var stats = services.UsageTracker.GetSessionStatistics(context.SessionId);
            if (stats.TotalInputTokens > 0 || stats.TotalOutputTokens > 0)
            {
                tokenInfo = $"  输入: {stats.TotalInputTokens:N0}\n  输出: {stats.TotalOutputTokens:N0}\n  总计: {stats.TotalInputTokens + stats.TotalOutputTokens:N0}";
            }
            else
            {
                tokenInfo = $"  {TerminalColors.Muted}暂无 Token 用量数据{AnsiStyleConstants.Reset}";
            }
        }
        else
        {
            tokenInfo = $"  {TerminalColors.Muted}用量追踪器不可用{AnsiStyleConstants.Reset}";
        }

        // 获取费用
        if (context.Services.CostTracker is not null)
        {
            var costStats = context.Services.CostTracker.GetTotalStatistics();
            if (costStats.TotalCostUsd > 0)
            {
                tokenInfo += $"\n  费用: ${costStats.TotalCostUsd:F4} USD";
            }
        }

        var apiStatus = string.IsNullOrEmpty(apiKey) ? $"{TerminalColors.Warning}未配置{AnsiStyleConstants.Reset}" : $"{TerminalColors.Success}已配置{AnsiStyleConstants.Reset}";
        var mcpStatus = hasMcpTools ? $"{TerminalColors.Muted}已注册{AnsiStyleConstants.Reset}" : $"{TerminalColors.Muted}未连接{AnsiStyleConstants.Reset}";

        var panel = new TabPanel(
            ["概览", "Token用量"],
            tabIndex => tabIndex switch
            {
                0 => RenderOverview(new RenderOverviewContext(version, cwd, context.SessionId, context.SessionStartedAt, duration, providerName, currentModel, isFastMode, effortLevel, apiStatus, mcpStatus, messageInfo)),
                1 => RenderTokenUsage(tokenInfo),
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);

        return ChatCommandResult.Continue();
    }

    private static string RenderOverview(RenderOverviewContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  版本:       {ctx.Version}");
        sb.AppendLine($"  工作目录:   {ctx.Cwd}");
        sb.AppendLine($"  会话ID:     {ctx.SessionId}");
        sb.AppendLine($"  开始时间:   {ctx.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"  持续时间:   {DurationFormatter.Format(ctx.Duration, new DurationFormatOptions { UseAbbreviations = false })}");
        sb.AppendLine($"  Provider:   {ctx.ProviderName}");
        sb.AppendLine($"  当前模型:   {ctx.CurrentModel}");
        sb.AppendLine($"  快速模式:   {(ctx.IsFastMode ? "开启" : "关闭")}");

        if (!string.IsNullOrEmpty(ctx.EffortLevel))
        {
            sb.AppendLine($"  推理力度:   {ctx.EffortLevel}");
        }

        sb.AppendLine($"  API密钥:    {ctx.ApiStatus}");
        sb.AppendLine($"  MCP工具:    {ctx.McpStatus}");
        sb.AppendLine();
        sb.AppendLine(ctx.MessageInfo);
        return sb.ToString();
    }

    private static string RenderTokenUsage(string tokenInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{TerminalColors.Accent}Token 用量{AnsiStyleConstants.Reset}");
        sb.AppendLine();
        sb.AppendLine(tokenInfo);
        return sb.ToString();
    }

    private static IProviderDefinition? ResolveProviderDefinition(ChatCommandContext context, string provider)
    {
        var registry = ChatCommandBase.GetService<IProviderDefinitionRegistry>(context, typeof(IProviderDefinitionRegistry));
        return registry?.TryGet(provider);
    }

    private static IModelCatalog ResolveModelCatalog(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("模型目录服务未初始化");
    }

}