namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3019: Task 变量赋值后未消费 — 'var x = SomeAsync();' 但 x 从未被 await/读取/return。
/// 比 JCC3015 更隐蔽的 fire-and-forget 变体：变量声明掩盖了返回值丢弃。
/// 通用检测：基于 BCL Task-like 类型判断 + 数据流分析（变量引用计数），不依赖项目特定方法名。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3019",
    Title = "异步红线: Task 变量赋值后未消费",
    Description = "变量 '{0}' 被赋值为异步调用 '{1}' 的返回值（Task/ValueTask），但从未被 await/读取/return。异步操作结果被丢弃，等价于 fire-and-forget。修复：await 该变量，或移除变量直接 '_ = SomeAsync()' 标注 fire-and-forget 意图。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = false,
    HelpLinkUri = "A variable is assigned a Task/ValueTask but never consumed (await/read/return). This is a hidden fire-and-forget — the variable declaration obscures the discarded return value. Fix: 1) await the variable; 2) return it; 3) or replace with '_ = SomeAsync()' to explicitly document fire-and-forget intent.")]
public sealed class TaskVariableUnusedRule : AnalyzerRuleBase<TaskVariableUnusedRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var body = methodDecl.Body;
        if (body is null) return;

        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol is not null && AotSafetyHelpers.IsDisposeMethod(methodSymbol)) return;

        var declarations = new Dictionary<ISymbol, (LocalDeclarationStatementSyntax Decl, InvocationExpressionSyntax Invocation, string VarName)>(SymbolEqualityComparer.Default);

        foreach (var localDecl in body.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()) {
            if (localDecl.Declaration.Variables.Count != 1) continue;
            var variable = localDecl.Declaration.Variables[0];
            if (variable.Initializer is null) continue;

            var invocation = variable.Initializer.Value as InvocationExpressionSyntax;
            if (invocation is null) continue;

            var calledSymbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (!AotSafetyHelpers.ReturnsTaskLike(calledSymbol)) continue;

            var varSymbol = ctx.SemanticModel.GetDeclaredSymbol(variable);
            if (varSymbol is null) continue;

            declarations[varSymbol] = (localDecl, invocation, variable.Identifier.ValueText);
        }

        if (declarations.Count == 0) return;

        var allIdentifierRefs = body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(id => !IsDeclarationTarget(id))
            .ToList();

            foreach (var kvp in declarations) {
                var varSymbol = kvp.Key;
                var decl = kvp.Value.Decl;
                var invocation = kvp.Value.Invocation;
                var varName = kvp.Value.VarName;
                var isConsumed = false;
            foreach (var idRef in allIdentifierRefs) {
                var refSymbol = ctx.SemanticModel.GetSymbolInfo(idRef).Symbol;
                if (!SymbolEqualityComparer.Default.Equals(refSymbol, varSymbol)) continue;

                if (idRef.Parent is AwaitExpressionSyntax) { isConsumed = true; break; }
                if (idRef.Parent is ReturnStatementSyntax) { isConsumed = true; break; }
                if (idRef.Parent is MemberAccessExpressionSyntax) { isConsumed = true; break; }
                if (idRef.Parent is ArgumentSyntax) { isConsumed = true; break; }
                if (idRef.Parent is AssignmentExpressionSyntax) { isConsumed = true; break; }
            }

            if (isConsumed) continue;

            var calledSymbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var calledName = calledSymbol?.ContainingType?.Name is { } tn ? $"{tn}.{calledSymbol.Name}" : invocation.Expression.ToString();
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), varName, calledName));
        }
    }

    private static bool IsDeclarationTarget(IdentifierNameSyntax id) {
        return id.Parent is VariableDeclaratorSyntax;
    }
}
