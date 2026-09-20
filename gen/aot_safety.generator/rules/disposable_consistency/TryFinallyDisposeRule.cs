namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9105: 资源释放 — try-finally 中手动 Dispose 应改用 using var / await using var。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9105",
    Title = "资源释放: try-finally 中手动 Dispose 应改用 using var / await using var",
    Description = "try-finally 块中手动调用 '{0}' 释放资源，应改用 'using var'（同步）或 'await using var'（异步）声明，由编译器自动展开 try-finally。手动 try-finally 易遗漏、样板冗余、且无法保证异常安全。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "AGENTS.md 规则1: 任何 IDisposable/IAsyncDisposable 对象在当前作用域内创建且不逃逸，必须用 'using var' / 'await using var' 声明。" +
    "正确做法: 1) 将变量声明改为 'using var x = new Xxx();' 或 'await using var x = new Xxx();'; 2) 删除手动 try-finally; 3) 编译器自动生成 try-finally 保证释放。" +
    "例外(不报告): a) try 块内有 return 语句(资源逃逸给调用方); b) 注释含 leave-open/escapes/factory/not-owning; c) 变量在 try 外声明且条件赋值(无法用 using var).")]
public sealed class TryFinallyDisposeRule : AnalyzerRuleBase<TryFinallyDisposeRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeTryFinallyDispose, SyntaxKind.TryStatement);
    }

    private static void AnalyzeTryFinallyDispose(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var tryStmt = (TryStatementSyntax)ctx.Node;
        if (tryStmt.Finally is null) return;

        var finallyBlock = tryStmt.Finally.Block;
        if (finallyBlock is null) return;

        if (tryStmt.Catches.Count > 0) return;

        if (finallyBlock.DescendantNodes().Any(n =>
            n.IsKind(SyntaxKind.ForEachStatement) || n.IsKind(SyntaxKind.ForEachVariableStatement))) return;

        var allInvocations = finallyBlock.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var disposeCalls = allInvocations.Where(IsDisposeCall).ToList();
        if (disposeCalls.Count == 0) return;

        if (allInvocations.Any(inv => !IsDisposeCall(inv))) return;

        if (ContainsReturnStatement(tryStmt.Block)) return;

        foreach (var call in disposeCalls) {
            if (!CanConvertToUsingVar(call, tryStmt, ctx.SemanticModel)) return;
        }

        var firstCall = disposeCalls[0];
        var callName = GetMemberName(firstCall);
        ctx.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            tryStmt.TryKeyword.GetLocation(),
            callName));
    }

    private static bool IsDisposeCall(InvocationExpressionSyntax invocation) {
        return GetMemberName(invocation) is "Dispose" or "DisposeAsync";
    }

    private static string GetMemberName(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        if (invocation.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText;
        return string.Empty;
    }

    private static bool ContainsReturnStatement(BlockSyntax? block) {
        if (block is null) return false;
        return block.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement) || n.IsKind(SyntaxKind.YieldReturnStatement));
    }

    private static bool CanConvertToUsingVar(
        InvocationExpressionSyntax disposeCall,
        TryStatementSyntax tryStmt,
        SemanticModel semanticModel) {
        if (disposeCall.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        var receiver = memberAccess.Expression;

        if (receiver is not IdentifierNameSyntax identifier) return false;

        var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
        if (symbol is null) return false;

        if (symbol is IFieldSymbol) return false;
        if (symbol is IParameterSymbol) return false;
        if (symbol is IPropertySymbol) return false;

        if (symbol is ILocalSymbol localSymbol) {
            var declRef = localSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (declRef is null) return false;
            var declNode = declRef.GetSyntax();
            if (declNode is not VariableDeclaratorSyntax varDecl) return false;

            if (varDecl.Initializer is null) return false;
            var initValue = varDecl.Initializer.Value;

            if (initValue.IsKind(SyntaxKind.NullLiteralExpression)) return false;
            if (initValue is DefaultExpressionSyntax) return false;

            if (!initValue.IsKind(SyntaxKind.ObjectCreationExpression) &&
                !initValue.IsKind(SyntaxKind.ImplicitObjectCreationExpression)) return false;

            if (declNode.SpanStart >= tryStmt.Span.Start) return true;

            return true;
        }

        return false;
    }
}
