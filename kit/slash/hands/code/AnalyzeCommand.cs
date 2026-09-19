
namespace JoinCode.ChatCommands;

/// <summary>
/// /analyze 命令 - 分析代码
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Analyze, Description = "分析代码", Usage = "/analyze <代码>", Category = ChatCommandCategory.Code, ExposeToMcp = true)]
[ChatCommandArg("code", Type = "string", Description = "要分析的代码内容", Required = true)]
public sealed partial class AnalyzeCommand : ChatCommandBase {
    private readonly ILogger<AnalyzeCommand>? _logger;
    /// <summary>
    /// 构造分析命令实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public AnalyzeCommand(ILogger<AnalyzeCommand>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 异步执行 /analyze 命令，分析给定代码
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        if (string.IsNullOrWhiteSpace(context.Arguments)) {
            TerminalHelper.WriteLine("请提供要分析的代码，例如: /analyze function test()");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine("正在分析代码...");
        var result = await context.GetCommandServices().CodeService.AnalyzeCodeAsync(context.Arguments, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine($"分析结果:\n{result}");

        return ChatCommandResult.Continue();
    }
}