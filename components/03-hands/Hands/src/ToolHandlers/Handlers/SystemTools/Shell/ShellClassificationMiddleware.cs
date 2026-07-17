namespace Tools.Shell;

/// <summary>
/// Shell 命令分类中间件 — 使用 ICommandClassifier 检测危险命令
/// 优先使用 Guard 的 ICommandClassifier（AST 解析），回退到 DestructiveCommandAnalyzer（正则）
/// </summary>
[Register]
public sealed partial class ShellClassificationMiddleware : IShellMiddleware
{
    [Inject] private readonly ICommandClassifier? _commandClassifier;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var dangerError = ClassifyCommand(context.Command, context.WorkingDirectory);
        if (dangerError != null)
        {
            context.ClassificationError = dangerError;
            context.Result = dangerError;
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }

    /// <summary>
    /// 使用 ICommandClassifier 对命令进行分类，返回危险命令的错误结果（或 null 表示安全）
    /// 优先使用 Guard 的 ICommandClassifier（AST 解析），回退到 DestructiveCommandAnalyzer（正则）
    /// </summary>
    private ToolResult? ClassifyCommand(string command, string? workingDirectory)
    {
        if (_commandClassifier is not null)
        {
            var shellCommand = ShellCommand.Parse(command);
            var classification = _commandClassifier.Classify(shellCommand, workingDirectory ?? string.Empty);

            if (classification.Category == CommandCategory.Destructive)
            {
                var warning = new StringBuilder();
                warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Potentially dangerous command detected");
                warning.AppendLine();
                if (!string.IsNullOrEmpty(classification.Details))
                {
                    warning.AppendLine(classification.Details);
                }
                if (classification.Risks.Count > 0)
                {
                    warning.AppendLine($"Risks: {string.Join(", ", classification.Risks)}");
                }
                warning.AppendLine();
                warning.AppendLine("If you are sure you want to execute this command, re-invoke and confirm you understand the risks.");

                return ResultBuilder.Error().WithText(warning.ToString()).Build();
            }

            if (classification.Category == CommandCategory.PathViolation)
            {
                var warning = new StringBuilder();
                warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Path violation detected");
                if (!string.IsNullOrEmpty(classification.Details))
                {
                    warning.AppendLine(classification.Details);
                }
                return ResultBuilder.Error().WithText(warning.ToString()).Build();
            }

            return null; // 安全命令
        }

        // 回退：使用 DestructiveCommandAnalyzer（正则匹配，无 AST 解析）
        var dangerAnalysis = DestructiveCommandAnalyzer.Analyze(command);
        if (dangerAnalysis.IsDangerous)
        {
            var warning = new StringBuilder();
            warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Potentially dangerous command detected");
            warning.AppendLine();
            warning.AppendLine(dangerAnalysis.WarningMessage);

            if (!string.IsNullOrEmpty(dangerAnalysis.Suggestion))
            {
                warning.AppendLine();
                warning.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Suggestion:");
                warning.AppendLine(dangerAnalysis.Suggestion);
            }

            warning.AppendLine();
            warning.AppendLine("Danger level: " + dangerAnalysis.Level);
            warning.AppendLine("If you are sure you want to execute this command, re-invoke and confirm you understand the risks.");

            return ResultBuilder.Error().WithText(warning.ToString()).Build();
        }

        return null;
    }
}
