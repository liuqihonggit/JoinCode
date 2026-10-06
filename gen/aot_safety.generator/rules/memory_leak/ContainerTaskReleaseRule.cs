namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9307: Task 集合未 WhenAll 等待 — 类型持有 Task/ValueTask 元素的集合字段,
/// 但 Dispose/DisposeAsync 未 await Task.WhenAll 等待所有任务完成。后台任务可能持有资源,未等待完成即释放会导致资源泄露。
/// 识别集合: List<T>/IList<T>/ICollection<T>/IEnumerable<T>/Dictionary<K,V>/ConcurrentDictionary<K,V>/T[] 等(T 是 Task/ValueTask)。
/// 完备释放识别: Dispose 体含 await Task.WhenAll(field) 或 Task.WhenAll(field)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9307",
    Title = "内存泄漏: Task 集合未 WhenAll 等待",
    Description = "类型持有 Task 元素的集合字段 '{0}',但 Dispose/DisposeAsync 未 await Task.WhenAll 等待所有任务完成。后台任务可能持有资源,未等待完成即释放会导致资源泄露。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "Task 集合字段必须在 Dispose/DisposeAsync 中 await Task.WhenAll 阻塞全部完成,确保后台任务持有的资源释放闭合。正确做法: " +
        "await Task.WhenAll(_tasks).ConfigureAwait(false); 或 await Task.WhenAll(_tasks.Values).ConfigureAwait(false); " +
        "若任务已保证完成(如通过 CancellationToken + 等待 Consumer 退出),在 PostStopAsync 中 WhenAll 残留任务。",
    IsCompilationEnd = false)]
public sealed class ContainerTaskReleaseRule : AnalyzerRuleBase<ContainerTaskReleaseRule> {
    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal) {
        "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "Collection",
    };
    private static readonly HashSet<string> DictionaryTypeNames = new(StringComparer.Ordinal) {
        "Dictionary", "ConcurrentDictionary", "IDictionary", "IReadOnlyDictionary",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeDeclaration(ctx),
            SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ctx.CancellationToken) as INamedTypeSymbol;
        if (typeSymbol is null) return;

        var taskFields = new List<IFieldSymbol>();
        foreach (var member in typeSymbol.GetMembers()) {
            if (member is not IFieldSymbol field) continue;
            if (field.IsStatic) continue;
            if (IsTaskElementContainer(field.Type))
                taskFields.Add(field);
        }
        if (taskFields.Count == 0) return;

        var disposeMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.Name is "Dispose" or "DisposeAsync" or "DisposeCore" or "DisposeAsyncCore"
                or "DisposeAsyncInternal" or "DisposeInternal" or "PostStopAsync")
            .ToList();
        if (disposeMethods.Count == 0) return;

        var compilation = ctx.SemanticModel.Compilation;
        foreach (var field in taskFields) {
            var released = false;
            foreach (var disposeMethod in disposeMethods) {
                var disposeDecl = disposeMethod.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax(ctx.CancellationToken) as MethodDeclarationSyntax;
                if (disposeDecl is null) continue;
                var disposeModel = compilation.GetSemanticModel(disposeDecl.SyntaxTree);
                if (disposeModel is null) continue;
                if (DisposeBodyAwaitsField(disposeDecl, field, disposeModel, compilation, ctx.CancellationToken)) {
                    released = true;
                    break;
                }
            }
            if (!released) {
                var fieldDecl = field.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax(ctx.CancellationToken) as VariableDeclaratorSyntax;
                if (fieldDecl is not null)
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, fieldDecl.Identifier.GetLocation(), field.Name));
            }
        }
    }

    private static bool IsTaskElementContainer(ITypeSymbol type) {
        if (type is IArrayTypeSymbol array)
            return IsTaskType(array.ElementType);
        if (type is not INamedTypeSymbol named) return false;
        var name = named.Name;
        var typeArgs = named.TypeArguments;
        if (CollectionTypeNames.Contains(name) && typeArgs.Length == 1)
            return IsTaskType(typeArgs[0]);
        if (DictionaryTypeNames.Contains(name) && typeArgs.Length == 2)
            return IsTaskType(typeArgs[1]);
        return false;
    }

    private static bool IsTaskType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol named) return false;
        return named.Name is "Task" or "ValueTask";
    }

    private static bool DisposeBodyAwaitsField(
        MethodDeclarationSyntax disposeMethod,
        IFieldSymbol field,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct) {
        if (disposeMethod.Body is null) return false;
        return BodyAwaitsField(disposeMethod.Body, field, semanticModel, compilation, ct, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private static bool BodyAwaitsField(
        BlockSyntax body,
        IFieldSymbol field,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct,
        HashSet<ISymbol> visited) {
        var fieldBackedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var varDecl in body.DescendantNodes().OfType<VariableDeclaratorSyntax>()) {
            if (varDecl.Initializer is null) continue;
            if (ReferencesField(varDecl.Initializer.Value, field, semanticModel, ct)) {
                var local = semanticModel.GetDeclaredSymbol(varDecl, ct);
                if (local is not null) fieldBackedLocals.Add(local);
            }
        }
        foreach (var assign in body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (ReferencesField(assign.Right, field, semanticModel, ct)) {
                var leftSymbol = semanticModel.GetSymbolInfo(assign.Left, ct).Symbol;
                if (leftSymbol is not null) fieldBackedLocals.Add(leftSymbol);
            }
        }
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return false;
            var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
            if (methodSymbol is null) continue;
            if (methodSymbol.Name == "WhenAll" && methodSymbol.ContainingType?.Name == "Task") {
                if (invocation.ArgumentList.Arguments.Count > 0
                    && ReferencesFieldOrLocal(invocation.ArgumentList.Arguments[0].Expression, field, fieldBackedLocals, semanticModel, ct))
                    return true;
            }
            if (!visited.Add(methodSymbol)) continue;
            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) continue;
            var methodDecl = syntaxRef.GetSyntax(ct) as MethodDeclarationSyntax;
            if (methodDecl?.Body is null) continue;
            var helperModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);
            if (helperModel is null) continue;
            if (BodyAwaitsField(methodDecl.Body, field, helperModel, compilation, ct, visited))
                return true;
        }
        return false;
    }

    private static bool ReferencesField(ExpressionSyntax expr, IFieldSymbol field, SemanticModel semanticModel, CancellationToken ct) {
        foreach (var node in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
        }
        return false;
    }

    private static bool ReferencesFieldOrLocal(ExpressionSyntax expr, IFieldSymbol field, HashSet<ISymbol> fieldBackedLocals, SemanticModel semanticModel, CancellationToken ct) {
        foreach (var node in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
            if (symbol is not null && fieldBackedLocals.Contains(symbol)) return true;
        }
        return false;
    }
}
