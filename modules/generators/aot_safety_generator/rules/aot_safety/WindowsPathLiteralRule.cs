namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1018: 跨平台安全 — 测试项目禁止 Windows 盘符路径字面量。
/// <para>事故背景（TASK070 CI #959）: 测试把 @"D:\cur\repo" 当普通数据喂给 Path.GetFullPath 归一逻辑，
/// Linux CI 上盘符/反斜杠语义分叉（GetFullPath 视为相对路径目录名），匹配静默失败 → CI 红而本地绿。</para>
/// <para>检测: 测试项目(ProjectType=test)中字符串字面量(含 verbatim/插值文本段)含盘符模式
/// [字母]:[\ 或 /]（前一字符非字母 — 排除 https:// 等协议）；非测试项目的 Windows 专属常量
/// (managed-settings 路径/注册表等)合法，不报。</para>
/// <para>修复导向: 测试路径用 Path.Combine(Path.GetTempPath(), ...) 跨平台构造；
/// 确需 Windows 路径行为测试用 #pragma warning disable JCC1018 显式豁免并注释理由。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1018",
    Title = "跨平台: 测试项目禁止 Windows 盘符路径字面量",
    Description = "测试代码含 Windows 盘符路径字面量 '{0}' — Linux CI 的 Path.GetFullPath 不识别盘符/反斜杠，归一化语义分叉会破坏匹配。用 Path.Combine(Path.GetTempPath(), ...) 跨平台构造；确需 Windows 路径行为测试用 #pragma warning disable JCC1018 豁免并注释理由。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Windows drive-letter path literals (e.g. @\"D:\\repo\") are platform-specific data. On Linux CI, Path.GetFullPath treats them as relative directory names, silently breaking path-matching tests. Build test paths cross-platform via Path.Combine(Path.GetTempPath(), ...); escape with #pragma warning disable JCC1018 only when the Windows path IS the behavior under test.")]
public sealed class WindowsPathLiteralRule : AnalyzerRuleBase<WindowsPathLiteralRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        // 仅测试项目 — 生产项目的 Windows 专属常量(managed-settings/注册表等)合法
        if (!projectContext.IsTest) {
            return;
        }

        context.RegisterSyntaxNodeAction(
            ctx => Analyze(ctx, projectContext),
            SyntaxKind.StringLiteralExpression,
            SyntaxKind.InterpolatedStringTextToken);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx, ProjectContext projectContext) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var text = ctx.Node switch {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InterpolatedStringTextSyntax interpolated => interpolated.TextToken.ValueText,
            _ => null,
        };
        if (text is not { Length: > 0 } literalText) return;

        if (!ContainsDriveLetterPattern(literalText)) return;

        var filePath = ctx.Node.SyntaxTree.FilePath;
        if (filePath.Contains("\\obj\\", StringComparison.Ordinal) || filePath.Contains("/obj/", StringComparison.Ordinal)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, ctx.Node.GetLocation(), text));
    }

    /// <summary>盘符模式扫描: [字母]:[\ 或 /] 且字母前一字符非字母（排除 https:// 等协议的单字母尾部）。</summary>
    internal static bool ContainsDriveLetterPattern(string text) {
        for (var i = 0; i < text.Length - 2; i++) {
            if (char.IsLetter(text[i])
                && text[i + 1] == ':'
                && (text[i + 2] == '\\' || text[i + 2] == '/')
                && (i == 0 || !char.IsLetter(text[i - 1]))) {
                return true;
            }
        }

        return false;
    }
}
