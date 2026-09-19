namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3006: .Result/.Wait() 阻塞调用可能导致死锁。测试项目豁免。
/// </summary>
[AnalyzerRule(
    Id = "JCC3006",
    Title = "异步红线: .Result/.Wait() 阻塞调用可能导致死锁",
    Description = "在异步上下文中调用 '{0}' 会阻塞当前线程等待 Task 完成，极易导致死锁。使用 await 替代。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = ".Result 和 .Wait() 会阻塞当前线程等待 Task 完成. 在有 SynchronizationContext 的环境 (ASP.NET Classic, WPF, WinForms) 中, Task 完成后需要回到原线程, 但原线程被阻塞, 形成死锁. 即使在无 SynchronizationContext 的环境 (ASP.NET Core, 控制台) 中, 也会浪费线程池线程. 正确做法: 始终使用 await. 例外: Main 方法入口和测试方法中可使用 .Result/.Wait().")]
public sealed class BlockingAsyncCallRule : AnalyzerRuleBase<BlockingAsyncCallRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var isTestProject = projectContext.IsTest;
        context.RegisterSyntaxNodeAction(
            ctx => Analyze(ctx, isTestProject),
            SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx, bool isTestProject) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;

        var memberName = memberAccess.Name.Identifier.ValueText;
        if (memberName != "Result" && memberName != "Wait") return;

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(memberAccess.Expression, ctx.CancellationToken);
        var symbol = symbolInfo.Symbol;
        if (symbol is null) return;

        var type = symbol switch {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol prop => prop.Type,
            IParameterSymbol param => param.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null,
        };

        if (type is null) return;

        var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var isTaskType = typeName.StartsWith("global::System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                         typeName.StartsWith("global::System.Threading.Tasks.ValueTask", StringComparison.Ordinal);

        if (!isTaskType) return;

        if (AotSafetyHelpers.IsInsideMainMethod(memberAccess)) return;
        if (isTestProject) return;
        if (AotSafetyHelpers.IsInsideConstructor(memberAccess)) return;
        if (AotSafetyHelpers.IsInsideSyncMethod(memberAccess)) return;
        if (AotSafetyHelpers.IsInsideDisposeMethod(memberAccess)) return;

        var callText = memberName == "Result" ? ".Result" : ".Wait()";
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, memberAccess.Name.GetLocation(), callText));
    }
}
