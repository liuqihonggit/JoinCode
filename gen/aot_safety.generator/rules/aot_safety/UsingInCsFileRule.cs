namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1005: .cs 文件内禁止写 using 语句。引用 Roslyn 的项目豁免(生成器需要 using)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1005",
    Title = "代码规范: .cs 文件内禁止写 using 语句",
    Description = "using 语句 '{0}' 应移动到 GlobalUsings.cs 统一管理，.cs 文件内禁止写 using。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "所有命名空间引用必须放在 GlobalUsings.cs 统一管理. 原因: 1) 避免重复引用; 2) 统一控制可见性; 3) 方便全局替换. 例外: GlobalUsings.cs 文件本身、生成的代码 (obj/ 目录).")]
public sealed class UsingInCsFileRule : AnalyzerRuleBase<UsingInCsFileRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var isRoslynProject = IsRoslynProject(context.Compilation);
        context.RegisterSyntaxNodeAction(
            ctx => Analyze(ctx, isRoslynProject),
            SyntaxKind.UsingDirective);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx, bool isRoslynProject) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not UsingDirectiveSyntax usingDirective) return;

        var filePath = usingDirective.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath)) return;
        if (filePath[0] == '/') return;
        if (filePath.EndsWith("GlobalUsings.cs", StringComparison.Ordinal)) return;
        if (filePath.Contains("\\obj\\", StringComparison.Ordinal) ||
            filePath.Contains("/obj/", StringComparison.Ordinal)) return;
        if (isRoslynProject) return;
        if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)) return;

        var usingName = usingDirective.Name?.ToString() ?? "?";
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, usingDirective.GetLocation(), usingName));
    }

    private static bool IsRoslynProject(Compilation compilation) {
        const string target = "Microsoft.CodeAnalysis.CSharp";
        var targetSpan = target.AsSpan();

        foreach (var reference in compilation.References) {
            if (reference is not PortableExecutableReference peRef) continue;
            var display = peRef.Display;
            if (display is null) continue;
            if (display.AsSpan().Contains(targetSpan, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
