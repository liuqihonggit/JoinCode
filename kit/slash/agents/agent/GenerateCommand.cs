
namespace JoinCode.ChatCommands;

/// <summary>
/// /generate 命令 - 生成代码
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Generate, Description = "生成代码", Usage = "/generate <描述>", Category = ChatCommandCategory.Agent)]
[ChatCommandArg("description", Type = "string", Description = "要生成的代码描述", Required = true)]
public sealed partial class GenerateCommand : ChatCommandBase
{
    private readonly ILogger<GenerateCommand>? _logger;

    /// <summary>
    /// 构造 GenerateCommand 实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public GenerateCommand(ILogger<GenerateCommand>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 执行 /generate 命令，根据描述生成代码
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        Diag.WriteLine($"[GenerateCommand] ExecuteAsync entry, Arguments='{context.Arguments}'");
        if (string.IsNullOrWhiteSpace(context.Arguments))
        {
            _logger?.LogWarning("请提供代码描述，例如: /generate 创建一个Hello World程序");
            TerminalHelper.WriteLine("请提供代码描述，例如: /generate 创建一个Hello World程序");
            Diag.WriteLine("[GenerateCommand] returning Continue (no args)");
            return ChatCommandResult.Continue();
        }

        _logger?.LogInformation("正在生成代码...");
        TerminalHelper.WriteLine("正在生成代码...");
        var result = await context.GetCommandServices().CodeService.GenerateCodeAsync(context.Arguments, context.CancellationToken);

        _logger?.LogInformation("生成的代码:\n==============\n{Result}", result);
        TerminalHelper.WriteLine($"生成的代码:\n==============\n{result}");

        return ChatCommandResult.Continue();
    }
}
