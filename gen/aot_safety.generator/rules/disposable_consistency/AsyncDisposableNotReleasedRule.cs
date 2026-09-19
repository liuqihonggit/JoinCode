namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9108: 资源泄漏 — IAsyncDisposable 局部变量创建后未释放。
/// 填补 Roslyn CA2000 不支持 IAsyncDisposable 的盲区。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9108",
    Title = "资源泄漏: IAsyncDisposable 局部变量创建后未释放",
    Description = "局部变量 '{0}' 类型实现 IAsyncDisposable，但未用 'await using' 声明，方法内也未手动调用 DisposeAsync()。将导致异步资源泄漏(后台任务/Channel/专用线程不退出)，CI 偶发卡死。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "IAsyncDisposable 对象必须释放,否则后台任务/Channel 消费者/专用线程永不退出,造成 CI 卡死。" +
    "正确做法: 1) 'var x = Create();' → 'await using var x = Create();'; " +
    "2) 若需手动释放: try { ... } finally { await x.DisposeAsync(); }; " +
    "3) 若所有权转移(返回/赋字段),添加注释 // not-owning 或 // escapes 标记豁免。" +
    "根因: Roslyn CA2000 不支持 IAsyncDisposable,此规则填补该盲区.")]
public sealed class AsyncDisposableNotReleasedRule : AnalyzerRuleBase<AsyncDisposableNotReleasedRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) return;

        var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        if (iasyncDisposableType is null) return;

        var containingMethod = localDecl.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod is null) return;

        foreach (var variable in localDecl.Declaration.Variables) {
            if (variable.Initializer is null) continue;
            if (variable.Initializer.Value.IsKind(SyntaxKind.NullLiteralExpression)) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(variable.Initializer.Value, ctx.CancellationToken);
            var type = typeInfo.Type as INamedTypeSymbol;
            if (type is null) continue;

            var implementsIAsyncDisposable = type.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            if (!implementsIAsyncDisposable) continue;

            var varName = variable.Identifier.ValueText;

            if (HasManualDisposeAsync(containingMethod, varName)) continue;
            if (IsOwnershipTransferred(containingMethod, varName)) continue;
            if (HasExemptionComment(localDecl)) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                variable.Identifier.GetLocation(),
                varName));
        }
    }

    /// <summary>
    /// 检查方法体内是否手动调用了 varName.DisposeAsync()。
    /// </summary>
    private static bool HasManualDisposeAsync(SyntaxNode method, string varName) {
        foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.ValueText == "DisposeAsync" &&
                ma.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == varName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查变量所有权是否转移给调用方/字段(return x / this.field = x)。
    /// </summary>
    private static bool IsOwnershipTransferred(SyntaxNode method, string varName) {
        foreach (var ret in method.DescendantNodes().OfType<ReturnStatementSyntax>()) {
            if (ret.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                return true;
        }

        foreach (var assign in method.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (assign.Right is IdentifierNameSyntax id && id.Identifier.ValueText == varName) {
                if (assign.Left is MemberAccessExpressionSyntax) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查声明是否有豁免注释(not-owning/escapes/factory/leave-open)。
    /// </summary>
    private static bool HasExemptionComment(LocalDeclarationStatementSyntax localDecl) {
        var trivia = localDecl.GetLeadingTrivia().ToString() + localDecl.GetTrailingTrivia().ToString();
        return trivia.Contains("not-owning") || trivia.Contains("escapes") ||
               trivia.Contains("factory") || trivia.Contains("leave-open");
    }
}
