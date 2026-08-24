namespace Tools.Shell;

/// <summary>
/// Shell 命令分类中间件 — 使用 ICommandClassifier 检测危险命令
/// 优先使用 Guard 的 ICommandClassifier（AST 解析），回退到 DestructiveCommandAnalyzer（正则）
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellClassificationMiddleware : ServiceEntity, IShellMiddleware
{

    public ShellClassificationMiddleware(ICommandClassifier? commandClassifier = null)
    {
        _commandClassifier = commandClassifier;
    }
    private readonly ICommandClassifier? _commandClassifier;

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

                var diag = BuildDestructiveCommandDiagnostic(command, classification.Details, classification.Risks.Select(r => r.ToString()).ToList());
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            if (classification.Category == CommandCategory.PathViolation)
            {
                var warning = new StringBuilder();
                warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Path violation detected");
                if (!string.IsNullOrEmpty(classification.Details))
                {
                    warning.AppendLine(classification.Details);
                }
                var diag = BuildPathViolationDiagnostic(command, classification.Details);
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            if (classification.Category == CommandCategory.ExcessiveSearchScope)
            {
                var warning = new StringBuilder();
                warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Search scope too large — command may hang or take very long");
                warning.AppendLine();
                if (!string.IsNullOrEmpty(classification.Details))
                {
                    warning.AppendLine(classification.Details);
                }
                warning.AppendLine();
                warning.AppendLine("Please restrict the search scope to a specific project directory.");
                warning.AppendLine("Avoid flags like --no-ignore/-u (rg) that bypass .gitignore rules.");
                warning.AppendLine("Avoid searching system root paths like C:\\, /, /home, etc.");

                var diag = BuildExcessiveSearchScopeDiagnostic(command, classification.Details);
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
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

            var fallbackDiag = BuildDestructiveCommandFallbackDiagnostic(command, dangerAnalysis.WarningMessage, dangerAnalysis.Level.ToString());
            return ToolResultBuilder.Error().WithText(fallbackDiag.FormattedMessage).WithDiagnostic(fallbackDiag).Build();
        }

        return null;
    }

    internal static ToolDiagnostic BuildDestructiveCommandDiagnostic(string command, string? details, IReadOnlyList<string> risks) =>
        ToolDiagnostic.Create(
            reason: "危险命令检测",
            formattedMessage: $"{StatusSymbol.Warning.ToValue()} Potentially dangerous command detected",
            details: BuildClassificationDetails(command, details, risks),
            suggestions: ["确认命令安全性后重新执行"]);

    internal static ToolDiagnostic BuildPathViolationDiagnostic(string command, string? details) =>
        ToolDiagnostic.Create(
            reason: "路径违规",
            formattedMessage: $"{StatusSymbol.Warning.ToValue()} Path violation detected",
            details: BuildClassificationDetails(command, details, []),
            suggestions: ["使用项目目录内的路径"]);

    internal static ToolDiagnostic BuildExcessiveSearchScopeDiagnostic(string command, string? details) =>
        ToolDiagnostic.Create(
            reason: "搜索范围过大",
            formattedMessage: $"{StatusSymbol.Warning.ToValue()} Search scope too large — command may hang or take very long",
            details: BuildClassificationDetails(command, details, []),
            suggestions:
            [
                "限制搜索范围到具体项目目录",
                "避免使用 --no-ignore/-u 等绕过 .gitignore 的标志",
                "避免搜索系统根路径如 C:\\, /, /home"
            ]);

    internal static ToolDiagnostic BuildDestructiveCommandFallbackDiagnostic(string command, string? warningMessage, string dangerLevel) =>
        ToolDiagnostic.Create(
            reason: "危险命令检测",
            formattedMessage: $"{StatusSymbol.Warning.ToValue()} Potentially dangerous command detected",
            details:
            [
                new DiagnosticDetail("command", command),
                new DiagnosticDetail("warning", warningMessage ?? string.Empty),
                new DiagnosticDetail("danger_level", dangerLevel)
            ],
            suggestions: ["确认命令安全性后重新执行"]);

    private static IReadOnlyList<DiagnosticDetail> BuildClassificationDetails(string command, string? details, IReadOnlyList<string> risks)
    {
        var list = new List<DiagnosticDetail> { new("command", command) };
        if (!string.IsNullOrEmpty(details)) list.Add(new DiagnosticDetail("details", details));
        if (risks.Count > 0) list.Add(new DiagnosticDetail("risks", string.Join(", ", risks)));
        return list;
    }
}
