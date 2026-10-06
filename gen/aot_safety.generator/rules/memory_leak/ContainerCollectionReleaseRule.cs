namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9306: 容器释放不完备 — 类型持有 IDisposable/IAsyncDisposable 元素的集合字段,
/// 但 Dispose/DisposeAsync 未遍历释放所有元素。容器 Dispose 漏掉任一元素将导致资源泄漏。
/// 识别集合: List<T>/IList<T>/ICollection<T>/IEnumerable<T>/Dictionary<K,V>/ConcurrentDictionary<K,V>/T[] 等。
/// 完备释放识别: Dispose 体含 foreach(x in field) x.Dispose() 或 foreach(x in field.Values) x.Dispose()。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9306",
    Title = "内存泄漏: 容器释放不完备",
    Description = "类型持有 IDisposable 元素的集合字段 '{0}',但 Dispose/DisposeAsync 未遍历释放所有元素。容器 Dispose 漏掉任一元素将导致资源泄漏。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "容器持有 IDisposable 元素时,Dispose/DisposeAsync 必须完备释放所有元素。正确做法: " +
        "1) foreach(var x in _items) x.Dispose(); / foreach(var x in _map.Values) x.DisposeAsync().ConfigureAwait(false); " +
        "2) Task 数组用 await Task.WhenAll(_tasks).ConfigureAwait(false) 阻塞全部完成(JCC9307); " +
        "3) 若容器元素是借用(容器不拥有),改容器元素类型为非 IDisposable 句柄(方案 D)。",
    IsCompilationEnd = false)]
public sealed class ContainerCollectionReleaseRule : AnalyzerRuleBase<ContainerCollectionReleaseRule> {
    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal) {
        "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "Collection",
    };
    private static readonly HashSet<string> DictionaryTypeNames = new(StringComparer.Ordinal) {
        "Dictionary", "ConcurrentDictionary", "IDictionary", "IReadOnlyDictionary",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeDeclaration(ctx, idisposableType, iasyncDisposableType),
            SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeTypeDeclaration(
        SyntaxNodeAnalysisContext ctx,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;
        if (idisposable is null && iasyncDisposable is null) return;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ctx.CancellationToken) as INamedTypeSymbol;
        if (typeSymbol is null) return;

        var containerFields = new List<IFieldSymbol>();
        foreach (var member in typeSymbol.GetMembers()) {
            if (member is not IFieldSymbol field) continue;
            if (field.IsStatic) continue;
            if (IsDisposableElementContainer(field.Type, idisposable, iasyncDisposable))
                containerFields.Add(field);
        }
        if (containerFields.Count == 0) return;

        var disposeMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.Name is "Dispose" or "DisposeAsync" or "DisposeCore" or "DisposeAsyncCore"
                or "DisposeAsyncInternal" or "DisposeInternal" or "PostStopAsync")
            .ToList();
        if (disposeMethods.Count == 0) return;

        var compilation = ctx.SemanticModel.Compilation;
        foreach (var field in containerFields) {
            var released = false;
            foreach (var disposeMethod in disposeMethods) {
                var disposeDecl = disposeMethod.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax(ctx.CancellationToken) as MethodDeclarationSyntax;
                if (disposeDecl is null) continue;
                var disposeModel = compilation.GetSemanticModel(disposeDecl.SyntaxTree);
                if (disposeModel is null) continue;
                if (DisposeBodyReleasesField(disposeDecl, field, disposeModel, compilation, ctx.CancellationToken)) {
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

    private static bool IsDisposableElementContainer(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
        if (type is IArrayTypeSymbol array)
            return IsDisposableType(array.ElementType, idisposable, iasyncDisposable);
        if (type is not INamedTypeSymbol named) return false;
        var name = named.Name;
        var typeArgs = named.TypeArguments;
        if (CollectionTypeNames.Contains(name) && typeArgs.Length == 1)
            return IsDisposableType(typeArgs[0], idisposable, iasyncDisposable);
        if (DictionaryTypeNames.Contains(name) && typeArgs.Length == 2)
            return IsDisposableType(typeArgs[1], idisposable, iasyncDisposable);
        return false;
    }

    private static bool IsDisposableType(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
        if (type is not INamedTypeSymbol named) return false;
        if (named.Name is "Task" or "ValueTask") return false;
        if (idisposable is not null && ImplementsInterface(named, idisposable)) return true;
        if (iasyncDisposable is not null && ImplementsInterface(named, iasyncDisposable)) return true;
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface) {
        if (SymbolEqualityComparer.Default.Equals(type, iface)) return true;
        foreach (var i in type.AllInterfaces) {
            if (SymbolEqualityComparer.Default.Equals(i, iface)) return true;
        }
        return false;
    }

    private static bool DisposeBodyReleasesField(
        MethodDeclarationSyntax disposeMethod,
        IFieldSymbol field,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct) {
        if (disposeMethod.Body is null) return false;
        return BodyReleasesField(disposeMethod.Body, field, semanticModel, compilation, ct, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private static bool BodyReleasesField(
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
        foreach (var foreachStmt in body.DescendantNodes().OfType<ForEachStatementSyntax>()) {
            if (ReferencesFieldOrLocal(foreachStmt.Expression, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var foreachStmt in body.DescendantNodes().OfType<ForEachVariableStatementSyntax>()) {
            if (ReferencesFieldOrLocal(foreachStmt.Expression, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return false;
            var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
            if (methodSymbol is null) continue;
            if (methodSymbol.Name == "WhenAll" && methodSymbol.ContainingType?.Name == "Task") {
                if (invocation.ArgumentList.Arguments.Count > 0
                    && ReferencesField(invocation.ArgumentList.Arguments[0].Expression, field, semanticModel, ct))
                    return true;
            }
            if (!visited.Add(methodSymbol)) continue;
            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) continue;
            var methodDecl = syntaxRef.GetSyntax(ct) as MethodDeclarationSyntax;
            if (methodDecl?.Body is null) continue;
            var helperModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);
            if (helperModel is null) continue;
            if (BodyReleasesField(methodDecl.Body, field, helperModel, compilation, ct, visited))
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
        if (expr is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Values")
            return ReferencesFieldOrLocal(ma.Expression, field, fieldBackedLocals, semanticModel, ct);
        var symbol = semanticModel.GetSymbolInfo(expr, ct).Symbol;
        if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
        if (symbol is not null && fieldBackedLocals.Contains(symbol)) return true;
        return false;
    }
}
