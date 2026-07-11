
namespace JoinCode.ChatCommands;

/// <summary>
/// /execute 命令 - 执行代码
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Execute, Description = "执行代码", Usage = "/execute <代码>", Category = ChatCommandCategory.Code)]
public sealed partial class ExecuteCommand : IChatCommand
{
    [Inject] private readonly ILogger<ExecuteCommand>? _logger;

    public string Name => ChatCommandNameConstants.Execute;
    public string Description => "执行代码";
    public string Usage => "/execute <代码>";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public ExecuteCommand(ILogger<ExecuteCommand>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Arguments))
        {
            // 兼容方案: 保留 LogWarning (logger 非 null 时记录日志)
            // 新增 TerminalHelper.WriteLine 确保用户可见反馈 (E2E 测试环境下 logger 为 null)
            _logger?.LogWarning("请提供要执行的代码，例如: /execute print('Hello World')");
            TerminalHelper.WriteLine("请提供要执行的代码，例如: /execute print('Hello World')");
            return ChatCommandResult.Continue();
        }

        _logger?.LogInformation("正在执行代码...");
        var result = await context.Services!.CodeService.ExecuteCodeAsync(context.Arguments, context.CancellationToken);

        _logger?.LogInformation("执行结果:\n==============\n{Result}", result);

        return ChatCommandResult.Continue();
    }
}
