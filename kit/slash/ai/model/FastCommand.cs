
namespace JoinCode.ChatCommands;

/// <summary>
/// /fast 命令 — 切换快速模式(使用更小/更快的模型)
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Fast, Description = "切换快速模式（使用更小/更快的模型）", Usage = "/fast [on|off]", Category = ChatCommandCategory.Model)]
[ChatCommandArg("state", Type = "string", Description = "开关状态", Enum = new[] { "on", "off" })]
public sealed class FastCommand : ToggleCommandBase
{
    /// <summary>命令名称</summary>
    public override string Name => ChatCommandNameEnumConstants.Fast;
    /// <summary>命令描述</summary>
    public override string Description => "切换快速模式（使用更小/更快的模型）";
    /// <summary>命令用法</summary>
    public override string Usage => "/fast [on|off]";

    /// <summary>
    /// 解析开关动作,支持 enable/disable/1/0 以及 on/off 别名
    /// </summary>
    /// <param name="args">用户输入的参数</param>
    /// <returns>解析得到的开关动作,无法识别时返回 null</returns>
    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "enable" or "1" => ToggleAction.On,
            "disable" or "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    /// <summary>无参数时的默认动作 — 显示当前状态</summary>
    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    /// <summary>
    /// 启用快速模式,激活 FastModeService 或设置 WorkflowConfig.FastMode 标志
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>表示启用操作的任务</returns>
    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.GetCommandServices().WorkflowConfig;

        if (fastModeService is not null)
        {
            fastModeService.Activate();
        }
        else if (config is not null)
        {
            config.FastMode = true;
        }

        var fastModel = fastModeService?.FastModelId ?? "fast model";
        TerminalHelper.WriteLine($"快速模式: 已启用 (使用 {fastModel})");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 禁用快速模式,停用 FastModeService 或清除 WorkflowConfig.FastMode 标志
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>表示禁用操作的任务</returns>
    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.GetCommandServices().WorkflowConfig;

        if (fastModeService is not null)
        {
            fastModeService.Deactivate();
        }
        else if (config is not null)
        {
            config.FastMode = false;
        }

        var primaryModel = fastModeService?.PrimaryModelId ?? "primary model";
        TerminalHelper.WriteLine($"快速模式: 已禁用 (使用 {primaryModel})");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 输出快速模式当前状态,包括是否启用与当前使用的模型
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>表示状态输出操作的任务</returns>
    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.GetCommandServices().WorkflowConfig;
        var isFast = fastModeService?.IsFastModeActive ?? config?.FastMode ?? false;
        var currentModel = isFast
            ? fastModeService?.FastModelId ?? "unknown"
            : fastModeService?.PrimaryModelId ?? "unknown";
        TerminalHelper.WriteLine($"快速模式: {(isFast ? "已启用" : "已禁用")} (当前模型: {currentModel})");
        TerminalHelper.WriteLine("使用 /fast on 启用，/fast off 禁用");

        return Task.CompletedTask;
    }
}
