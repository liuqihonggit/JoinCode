namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3005: async void 方法异常无法被捕获。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3005",
    Title = "异步红线: async void 方法异常无法被捕获",
    Description = "async void 方法的异常会直接炸掉进程，调用者无法捕获。改为 async Task 返回类型。（UI 事件处理器除外）",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Error,
    HelpLinkUri = "async void 方法的异常不会传播到调用者, 而是直接在 SynchronizationContext 上抛出, 导致应用崩溃. 正确做法: 1) 改为 async Task; 2) UI 事件处理器 (如 Button_Click) 是唯一例外; 3) 如果必须 fire-and-forget, 使用 'async Task' + '_ = MethodAsync()' 模式, 配合 CancellationToken 保护.")]
public sealed class AsyncVoidRule : AnalyzerRuleBase<AsyncVoidRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not MethodDeclarationSyntax methodDecl) return;

        if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;
        if (methodDecl.ReturnType is not PredefinedTypeSyntax predefined ||
            !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)) return;

        var methodName = methodDecl.Identifier.ValueText;
        if (AotSafetyHelpers.IsUiEventHandler(methodName)) return;
        if (AotSafetyHelpers.IsTimerCallbackPattern(methodName)) return;

        if (methodDecl.AttributeLists.Any(al =>
            al.Attributes.Any(a =>
                a.Name.ToString().Contains("EventHandler", StringComparison.Ordinal) ||
                a.Name.ToString().Contains("Handles", StringComparison.Ordinal))))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, methodDecl.ReturnType.GetLocation()));
    }
}
