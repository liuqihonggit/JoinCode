namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9107: 资源释放 — IAsyncDisposable 对象禁止同步 Dispose，必须用 DisposeAsync 收拢异步释放。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9107",
    Title = "资源释放: IAsyncDisposable 对象禁止同步 Dispose，必须用 DisposeAsync 收拢异步释放",
    Description = "对 IAsyncDisposable 类型 '{0}' 调用同步 Dispose()，跳过了异步清理逻辑，导致资源泄露。必须收拢为 await DisposeAsync() 或 await using var 声明。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "AGENTS.md 规则1: IAsyncDisposable 对象必须用异步释放。" +
    "正确做法: 1) 'x.Dispose()' → 'await x.DisposeAsync().ConfigureAwait(false)'; 2) 'using var x = ...' → 'await using var x = ...'; 3) try-finally 中 'x.Dispose()' → 'await x.DisposeAsync()'." +
    "原因: 同步 Dispose 不会调用 DisposeAsync，异步清理逻辑(如 flush buffer、close connection gracefully)被完全跳过，造成句柄泄露/数据丢失.")]
public sealed class SyncDisposeOnAsyncDisposableRule : AnalyzerRuleBase<SyncDisposeOnAsyncDisposableRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeSyncDisposeOnAsyncDisposable, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeSyncDisposeOnAsyncDisposable(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (GetMemberName(invocation) is not "Dispose") return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        var receiver = memberAccess.Expression;

        var receiverType = ctx.SemanticModel.GetTypeInfo(receiver, ctx.CancellationToken).Type as INamedTypeSymbol;
        if (receiverType is null) return;

        var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
        if (iasyncDisposableType is null || idisposableType is null) return;

        var implementsIAsyncDisposable = receiverType.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default)
            || SymbolEqualityComparer.Default.Equals(receiverType, iasyncDisposableType);
        if (!implementsIAsyncDisposable) return;

        var implementsIDisposable = receiverType.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default)
            || SymbolEqualityComparer.Default.Equals(receiverType, idisposableType);
        // 双接口类型(IDisposable + IAsyncDisposable)用同步 Dispose 是合法的
        if (implementsIDisposable) return;

        if (IsInsideDisposeMethod(invocation)) return;
        if (IsInsideLambda(invocation)) return;

        var typeName = receiverType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        ctx.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            typeName));
    }

    private static bool IsInsideDisposeMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax method) {
                var name = method.Identifier.ValueText;
                if (name is "Dispose" or "DisposeAsync")
                    return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static bool IsInsideLambda(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax) return true;
            current = current.Parent;
        }
        return false;
    }

    private static string GetMemberName(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        if (invocation.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText;
        return string.Empty;
    }

    private static bool IsWhitelistedDualInterface(INamedTypeSymbol type) {
        var fullName = type.OriginalDefinition.ToDisplayString();
        return fullName is "System.Threading.CancellationTokenSource"
            or "System.IO.MemoryStream"
            or "System.Threading.CancellationTokenRegistration";
    }
}
