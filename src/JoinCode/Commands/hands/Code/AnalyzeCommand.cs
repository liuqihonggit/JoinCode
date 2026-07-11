
namespace JoinCode.ChatCommands;

/// <summary>
/// /analyze 命令 - 分析代码
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Analyze, Description = "分析代码", Usage = "/analyze <代码>", Category = ChatCommandCategory.Code)]
public sealed partial class AnalyzeCommand : IChatCommand
{
    [Inject] private readonly ILogger<AnalyzeCommand>? _logger;

    public string Name => ChatCommandNameConstants.Analyze;
    public string Description => "分析代码";
    public string Usage => "/analyze <代码>";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public AnalyzeCommand(ILogger<AnalyzeCommand>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Arguments))
        {
            TerminalHelper.WriteLine("请提供要分析的代码，例如: /analyze function test()");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine("正在分析代码...");
        var result = await context.Services!.CodeService.AnalyzeCodeAsync(context.Arguments, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine($"分析结果:\n{result}");

        return ChatCommandResult.Continue();
    }
}
