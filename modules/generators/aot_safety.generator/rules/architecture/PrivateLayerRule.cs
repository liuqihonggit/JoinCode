namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC12013: <c>private/</c> 层次违规 — <c>private/</c> 目录用于包裹项目私有实现，
/// 其中声明的类型不得是 <c>public</c>。
/// <para>修复导向: 把 <c>private/</c> 下的 public 类型改为 internal；确实需要跨程序集访问时，
/// 先评估是否应移到上层公开层，而不是从 private 目录直接暴露。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC12013",
    Title = "private/ 层次违规",
    Description = "类型 '{0}' 位于 private/ 目录却声明为 public。private/ 包裹项目私有实现，内部类型应使用 internal。",
    Category = "Architecture",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Types declared under a private/ directory are project-private implementation and must not be public. Mark them internal, or move the public contract to an upper layer.",
    IsCompilationEnd = false)]
public sealed class PrivateLayerRule : AnalyzerRuleBase<PrivateLayerRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.EnumDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var declaration = context.Node;
        if (!IsInPrivateDirectory(declaration.SyntaxTree.FilePath)) return;
        if (declaration is not BaseTypeDeclarationSyntax typeDeclaration) return;

        var publicKeyword = typeDeclaration.Modifiers.FirstOrDefault(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword));
        if (publicKeyword.IsKind(SyntaxKind.None)) return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, publicKeyword.GetLocation(), typeDeclaration.Identifier.Text));
    }

    private static bool IsInPrivateDirectory(string filePath) {
        if (filePath.Length == 0) return false;
        var normalized = filePath.Replace('\\', '/');
        return normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase)
            || normalized.IndexOf("/private/", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
