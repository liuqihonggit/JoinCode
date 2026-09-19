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

            if (IsFetchedNotCreated(variable.Initializer.Value)) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(variable.Initializer.Value, ctx.CancellationToken);
            var type = typeInfo.Type as INamedTypeSymbol;
            if (type is null) continue;

            var implementsIAsyncDisposable = type.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            if (!implementsIAsyncDisposable) continue;

            var varName = variable.Identifier.ValueText;

            if (HasManualDisposeAsync(containingMethod, varName)) continue;
            if (IsOwnershipTransferred(containingMethod, varName, ctx.SemanticModel, ctx.CancellationToken)) continue;
            if (IsCapturedByAwaitUsing(containingMethod, varName)) continue;
            if (IsCapturedByUsingStatement(containingMethod, varName)) continue;
            if (IsCapturedByUsingVar(containingMethod, varName)) continue;
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
    private static bool IsOwnershipTransferred(SyntaxNode method, string varName, SemanticModel? semanticModel = null, CancellationToken ct = default) {
        foreach (var ret in method.DescendantNodes().OfType<ReturnStatementSyntax>()) {
            if (ret.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                return true;
            if (ret.Expression is not null && ContainsIdentifier(ret.Expression, varName))
                return true;
        }

        foreach (var assign in method.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (assign.Right is IdentifierNameSyntax id && id.Identifier.ValueText == varName) {
                if (assign.Left is MemberAccessExpressionSyntax) return true;
                if (assign.Left is ElementAccessExpressionSyntax) return true;
                if (assign.Left is IdentifierNameSyntax leftId && semanticModel is not null) {
                    var symbol = semanticModel.GetSymbolInfo(leftId, ct).Symbol;
                    if (symbol is IFieldSymbol) return true;
                }
            }
        }

        foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            string? methodName = null;
            if (inv.Expression is MemberAccessExpressionSyntax ma)
                methodName = ma.Name.Identifier.ValueText;
            else if (inv.Expression is IdentifierNameSyntax invIdent)
                methodName = invIdent.Identifier.ValueText;

            if (methodName is not null) {
                if (methodName is "Add" or "AddRange" or "Insert" or "Push" or "Enqueue" or "TryAdd" or "SetResult" or "SetException") {
                    if (inv.ArgumentList.Arguments.Any(arg => IsIdentifier(arg.Expression, varName)))
                        return true;
                }
                if (methodName.StartsWith("Load", StringComparison.Ordinal) ||
                    methodName.StartsWith("Register", StringComparison.Ordinal) ||
                    methodName.StartsWith("Start", StringComparison.Ordinal) ||
                    methodName.StartsWith("Spawn", StringComparison.Ordinal) ||
                    methodName.StartsWith("Enqueue", StringComparison.Ordinal) ||
                    methodName.StartsWith("Schedule", StringComparison.Ordinal)) {
                    if (inv.ArgumentList.Arguments.Any(arg => IsIdentifier(arg.Expression, varName)))
                        return true;
                }
            }
        }

        if (IsReferencedInLambda(method, varName)) return true;

        return false;
    }

    /// <summary>
    /// 判断初始化表达式是否为"获取而非新建"——从字段/属性/集合索引/缓存方法获取,不拥有所有权。
    /// </summary>
    private static bool IsFetchedNotCreated(SyntaxNode initValue) {
        if (initValue is MemberAccessExpressionSyntax) return true;
        if (initValue is ElementAccessExpressionSyntax) return true;
        if (initValue is IdentifierNameSyntax) return true;
        if (initValue is CastExpressionSyntax) return true;
        if (initValue is ParenthesizedExpressionSyntax paren) return IsFetchedNotCreated(paren.Expression);

        if (initValue is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression)) {
            if (IsFetchedNotCreated(binary.Left)) return true;
        }

        if (initValue is InvocationExpressionSyntax syncInv) {
            string? methodName = null;
            if (syncInv.Expression is MemberAccessExpressionSyntax syncMA)
                methodName = syncMA.Name.Identifier.ValueText;
            else if (syncInv.Expression is IdentifierNameSyntax syncIdent)
                methodName = syncIdent.Identifier.ValueText;

            if (methodName is not null &&
                (methodName.StartsWith("Get", StringComparison.Ordinal) ||
                methodName.StartsWith("TryGet", StringComparison.Ordinal) ||
                methodName.StartsWith("Acquire", StringComparison.Ordinal) ||
                methodName.StartsWith("Peek", StringComparison.Ordinal) ||
                methodName.StartsWith("Borrow", StringComparison.Ordinal) ||
                methodName.StartsWith("Resolve", StringComparison.Ordinal) ||
                methodName.StartsWith("Lookup", StringComparison.Ordinal) ||
                methodName.Contains("GetOrCreate") ||
                methodName.Contains("GetOrAdd")))
                return true;
        }

        if (initValue is AwaitExpressionSyntax awaitExpr) {
            var inner = awaitExpr.Expression;
            if (inner is InvocationExpressionSyntax configureAwaitInv &&
                configureAwaitInv.Expression is MemberAccessExpressionSyntax configureAwaitMA &&
                configureAwaitMA.Name.Identifier.ValueText == "ConfigureAwait") {
                inner = configureAwaitMA.Expression;
            }

            if (inner is InvocationExpressionSyntax inv) {
                string? methodName = null;
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                    methodName = ma.Name.Identifier.ValueText;
                else if (inv.Expression is IdentifierNameSyntax ident)
                    methodName = ident.Identifier.ValueText;

                if (methodName is not null &&
                    (methodName.StartsWith("Get", StringComparison.Ordinal) ||
                    methodName.StartsWith("TryGet", StringComparison.Ordinal) ||
                    methodName.StartsWith("Acquire", StringComparison.Ordinal) ||
                    methodName.StartsWith("Peek", StringComparison.Ordinal) ||
                    methodName.StartsWith("Borrow", StringComparison.Ordinal) ||
                    methodName.StartsWith("Resolve", StringComparison.Ordinal) ||
                    methodName.StartsWith("Lookup", StringComparison.Ordinal) ||
                    methodName.Contains("GetOrCreate") ||
                    methodName.Contains("GetOrAdd")))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查变量是否被 await using 通过 ConfigureAwait 模式接管:
    /// `await using var y = x.ConfigureAwait(false)` — x 被 y 的 await using 释放。
    /// </summary>
    private static bool IsCapturedByAwaitUsing(SyntaxNode method, string varName) {
        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()) {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;
            if (!localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) continue;

            foreach (var var in localDecl.Declaration.Variables) {
                if (var.Initializer?.Value is not InvocationExpressionSyntax inv) continue;
                if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
                if (ma.Name.Identifier.ValueText != "ConfigureAwait") continue;
                if (ma.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 检查变量是否被 using var 间接接管: `using var y = SomeMethod(x)` — x 被 y 的 using 释放。
    /// </summary>
    private static bool IsCapturedByUsingVar(SyntaxNode method, string varName) {
        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()) {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;

            foreach (var var in localDecl.Declaration.Variables) {
                if (var.Initializer is null) continue;
                if (ContainsIdentifier(var.Initializer.Value, varName))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 检查变量是否被 await using 块语句接管: `await using (x) { ... }`。
    /// </summary>
    private static bool IsCapturedByUsingStatement(SyntaxNode method, string varName) {
        foreach (var usingStmt in method.DescendantNodes().OfType<UsingStatementSyntax>()) {
            if (usingStmt.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                return true;
        }
        return false;
    }

    private static bool IsReferencedInLambda(SyntaxNode method, string varName) {
        foreach (var lambda in method.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>()) {
            if (ContainsIdentifier(lambda, varName)) return true;
        }
        foreach (var lambda in method.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>()) {
            if (ContainsIdentifier(lambda, varName)) return true;
        }
        foreach (var localFunc in method.DescendantNodes().OfType<LocalFunctionStatementSyntax>()) {
            if (localFunc.Body is not null && ContainsIdentifier(localFunc.Body, varName)) return true;
        }
        return false;
    }

    private static bool ContainsIdentifier(SyntaxNode node, string varName) {
        foreach (var id in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
            if (id.Identifier.ValueText == varName) return true;
        }
        return false;
    }

    private static bool IsIdentifier(SyntaxNode node, string varName) {
        return node is IdentifierNameSyntax id && id.Identifier.ValueText == varName;
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
