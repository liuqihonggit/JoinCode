namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4004: 死锁风险 — lock 和 SemaphoreSlim 保护同一状态（竞态条件）。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4004",
    Title = "死锁风险: lock 和 SemaphoreSlim 保护同一状态（竞态条件）",
    Description = "字段 '{0}' 同时被 lock 语句和 SemaphoreSlim '{1}' 保护，存在竞态条件. 应统一使用 SemaphoreSlim 替代 lock，确保异步和同步路径使用同一把锁.",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "lock 和 SemaphoreSlim 是两种不同的锁机制，同时保护同一状态时: 1) lock 是线程独占锁，SemaphoreSlim 是信号量; 2) 两者互不感知，无法保证互斥; 3) 异步方法通过 SemaphoreSlim 获取锁，同步方法通过 lock 获取锁，两者可以同时进入临界区.正确做法: 统一使用 SemaphoreSlim(1,1)，同步路径用 Wait(0)，异步路径用 WaitAsync().")]
public sealed class LockAndSemaphoreSlimOnSameStateRule : AnalyzerRuleBase<LockAndSemaphoreSlimOnSameStateRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLockAndSemaphoreSlimOnSameState, SyntaxKind.LockStatement);
    }

    private static void AnalyzeLockAndSemaphoreSlimOnSameState(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var lockStatement = (LockStatementSyntax)ctx.Node;

        var lockExpression = lockStatement.Expression;
        if (lockExpression is null) return;

        var typeDecl = AotSafetyHelpers.FindEnclosingTypeDeclaration(lockStatement);
        if (typeDecl is null) return;

        var lockFieldName = GetIdentifierName(lockExpression);
        if (lockFieldName is null) return;

        if (!lockFieldName.StartsWith("_", StringComparison.Ordinal) && !IsThisMemberAccess(lockExpression))
            return;

        var semaphoreFields = typeDecl.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(f => IsSemaphoreSlimField(f, ctx.SemanticModel))
            .ToList();

        if (semaphoreFields.Count == 0) return;

        foreach (var semField in semaphoreFields) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var semFieldName = semField.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText;
            if (semFieldName is null) continue;

            if (lockFieldName == semFieldName) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor,
                lockStatement.GetLocation(), lockFieldName, semFieldName));

            return;
        }
    }

    private static bool IsThisMemberAccess(ExpressionSyntax expression) {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is ThisExpressionSyntax)
            return true;
        return false;
    }

    private static string? GetIdentifierName(ExpressionSyntax expression) {
        if (expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText;
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        return null;
    }

    private static bool IsSemaphoreSlimField(FieldDeclarationSyntax fieldDecl, SemanticModel semanticModel) {
        var typeInfo = semanticModel.GetTypeInfo(fieldDecl.Declaration.Type);
        return typeInfo.Type?.Name == "SemaphoreSlim";
    }
}
