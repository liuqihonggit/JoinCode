
namespace JoinCode.ChatCommands;

/// <summary>
/// /generate 命令 - 生成代码
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Generate, Description = "生成代码", Usage = "/generate <描述>", Category = ChatCommandCategory.Agent)]
public sealed partial class GenerateCommand : IChatCommand
{
    [Inject] private readonly ILogger<GenerateCommand>? _logger;

    public string Name => ChatCommandNameConstants.Generate;
    public string Description => "生成代码";
    public string Usage => "/generate <描述>";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public GenerateCommand(ILogger<GenerateCommand>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Arguments))
        {
            _logger?.LogWarning("请提供代码描述，例如: /generate 创建一个Hello World程序");
            TerminalHelper.WriteLine("请提供代码描述，例如: /generate 创建一个Hello World程序");
            return ChatCommandResult.Continue();
        }

        _logger?.LogInformation("正在生成代码...");
        TerminalHelper.WriteLine("正在生成代码...");
        var result = await context.Services.CodeService.GenerateCodeAsync(context.Arguments, context.CancellationToken);

        _logger?.LogInformation("生成的代码:\n==============\n{Result}", result);
        TerminalHelper.WriteLine($"生成的代码:\n==============\n{result}");

        return ChatCommandResult.Continue();
    }
}
