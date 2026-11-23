namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC8001: 代码组织 — 文件行数超过2000行，建议拆分。
/// 用 RegisterSemanticModelAction 替代 RegisterSyntaxTreeAction(IAnalyzerRule.Register 接收 CompilationStartAnalysisContext)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC8001",
    Title = "代码组织: 文件行数超过2000行，建议拆分",
    Description = "文件 '{0}' 有 {1} 行，超过2000行阈值。过长的文件难以维护和阅读，应按职责拆分为多个类或使用 partial class 分文件组织。",
    Category = "CodeOrganization",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Files exceeding 2000 lines typically carry too many responsibilities. Split strategies: 1) Extract independent classes by responsibility; 2) Use partial class to split one class across files; 3) Extract helper methods to extension classes; 4) Extract nested classes to separate files.")]
public sealed class FileTooLongRule : AnalyzerRuleBase<FileTooLongRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSemanticModelAction(AnalyzeFileTooLong);
    }

    private static void AnalyzeFileTooLong(SemanticModelAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var tree = ctx.SemanticModel.SyntaxTree;
        var lineCount = tree.GetText().Lines.Count;

        if (lineCount <= 2000) return;

        var filePath = tree.FilePath;
        var lastSlash = filePath.LastIndexOfAny(new[] { '\\', '/' });
        var fileName = lastSlash >= 0 ? filePath.Substring(lastSlash + 1) : filePath;
        var location = tree.GetRoot().GetLocation();

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, fileName, lineCount));
    }
}
