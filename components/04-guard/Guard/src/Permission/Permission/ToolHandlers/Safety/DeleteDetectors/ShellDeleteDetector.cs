
namespace Core.Permission;

/// <summary>
/// Shell 删除命令检测器 — 检测 Shell/PowerShell 中的文件删除命令（rm/del/Remove-Item 等）
/// 利用已有的 IDestructiveCommandDetector 检测 CommandRisk.FileDeletion 风险
/// </summary>
[Register(typeof(IDeleteOperationDetector))]
public sealed partial class ShellDeleteDetector : IDeleteOperationDetector
{
    private static readonly FrozenSet<string> DeleteToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ShellToolNameConstants.ShellExecute,
        ShellToolNameConstants.Powershell
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DeleteCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "erase", "Remove-Item", "rmdir", "rd"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IDestructiveCommandDetector? _destructiveCommandDetector;

    public ShellDeleteDetector(IDestructiveCommandDetector? destructiveCommandDetector = null)
    {
        _destructiveCommandDetector = destructiveCommandDetector;
    }

    /// <inheritdoc />
    public DeleteOperationInfo? Detect(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        if (!DeleteToolNames.Contains(toolName))
            return null;

        if (arguments is null || !arguments.TryGetValue("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
            return null;

        var command = cmdEl.GetString()!;
        return DetectDeleteCommand(command);
    }

    /// <summary>
    /// 检测命令是否为删除操作
    /// </summary>
    private DeleteOperationInfo? DetectDeleteCommand(string command)
    {
        if (_destructiveCommandDetector is not null)
        {
            var shellCommand = ShellCommand.Parse(command);
            var result = _destructiveCommandDetector.Detect(shellCommand);

            if (!result.IsDestructive || !result.Risks.Contains(CommandRisk.FileDeletion))
                return null;

            var targetPath = shellCommand.ReferencedPaths.FirstOrDefault();

            return new DeleteOperationInfo
            {
                TargetPath = targetPath,
                SourceDescription = $"Shell {shellCommand.CommandName} 命令"
            };
        }

        return DetectByCommandName(command);
    }

    /// <summary>
    /// 降级检测 — 无 IDestructiveCommandDetector 时通过命令名匹配
    /// </summary>
    private DeleteOperationInfo? DetectByCommandName(string command)
    {
        var firstWord = command.Split(' ', 2)[0];
        var commandName = firstWord.TrimStart('-', '/');

        if (!DeleteCommandNames.Contains(commandName))
            return null;

        var paths = ShellCommand.Parse(command).ReferencedPaths;
        var targetPath = paths.FirstOrDefault();

        return new DeleteOperationInfo
        {
            TargetPath = targetPath,
            SourceDescription = $"Shell {commandName} 命令"
        };
    }
}
