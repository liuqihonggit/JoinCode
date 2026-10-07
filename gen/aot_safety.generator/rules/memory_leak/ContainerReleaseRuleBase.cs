namespace AotSafety.Generator.Rules;

/// <summary>
/// 容器释放规则基类 — JCC9306/JCC9307 共享框架。
/// 状态机驱动:Unknown → Owned/Skip → Released/Leaked。
/// 模板方法:子类提供 IsTargetField(字段筛选)和 CreateReleaseDetector(释放模式检测)。
/// 共享:集合类型名、Dispose 方法发现、field-backed locals 收集、调用链递归、字段引用判定。
/// </summary>
public abstract class ContainerReleaseRuleBase<T> : AnalyzerRuleBase<T> where T : IAnalyzerRule {
    /// <summary>容器字段生命周期状态机</summary>
    protected enum ContainerFieldLifecycle {
        /// <summary>未分析</summary>
        Unknown,
        /// <summary>拥有:容器/字段持有目标元素,当前类型负责释放</summary>
        Owned,
        /// <summary>跳过:非目标字段</summary>
        Skip,
        /// <summary>已释放:Dispose 体中检测到释放模式</summary>
        Released,
        /// <summary>泄露:拥有但未释放</summary>
        Leaked,
    }

    /// <summary>释放模式检测器 — 检查方法体中是否包含释放该字段的模式(foreach/WhenAll/await 等)</summary>
    protected delegate bool ReleaseDetector(
        BlockSyntax body,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct);

    protected static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal) {
        "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "Collection",
    };
    protected static readonly HashSet<string> DictionaryTypeNames = new(StringComparer.Ordinal) {
        "Dictionary", "ConcurrentDictionary", "IDictionary", "IReadOnlyDictionary",
    };

    private static readonly HashSet<string> DisposeMethodNames = new(StringComparer.Ordinal) {
        "Dispose", "DisposeAsync", "DisposeCore", "DisposeAsyncCore",
        "DisposeAsyncInternal", "DisposeInternal", "PostStopAsync",
    };

    /// <summary>子类实现:判定字段是否目标(容器持有目标元素 or 单个目标字段)</summary>
    protected abstract bool IsTargetField(IFieldSymbol field, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable);

    /// <summary>子类实现:创建释放模式检测器(foreach/WhenAll/await 等组合)</summary>
    protected abstract ReleaseDetector CreateReleaseDetector();

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeDeclaration(ctx, idisposableType, iasyncDisposableType),
            SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private void AnalyzeTypeDeclaration(
        SyntaxNodeAnalysisContext ctx,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ctx.CancellationToken) as INamedTypeSymbol;
        if (typeSymbol is null) return;

        var targetFields = CollectTargetFields(typeSymbol, idisposable, iasyncDisposable);
        if (targetFields.Count == 0) return;

        var disposeMethods = GetDisposeMethods(typeSymbol);
        if (disposeMethods.Count == 0) return;

        var compilation = ctx.SemanticModel.Compilation;
        var detector = CreateReleaseDetector();

        foreach (var field in targetFields) {
            var lifecycle = AnalyzeFieldLifecycle(field, disposeMethods, compilation, ctx.CancellationToken, detector);
            if (lifecycle == ContainerFieldLifecycle.Leaked)
                ReportLeak(ctx, field);
        }
    }

    /// <summary>状态机驱动:Owned(已筛选)→ Released/Leaked</summary>
    private static ContainerFieldLifecycle AnalyzeFieldLifecycle(
        IFieldSymbol field,
        List<IMethodSymbol> disposeMethods,
        Compilation compilation,
        CancellationToken ct,
        ReleaseDetector detector) {
        foreach (var disposeMethod in disposeMethods) {
            var disposeDecl = disposeMethod.DeclaringSyntaxReferences
                .FirstOrDefault()?.GetSyntax(ct) as MethodDeclarationSyntax;
            if (disposeDecl?.Body is null) continue;
            var disposeModel = compilation.GetSemanticModel(disposeDecl.SyntaxTree);
            if (disposeModel is null) continue;

            if (BodyReleasesField(disposeDecl.Body, field, disposeModel, compilation, ct,
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default), detector))
                return ContainerFieldLifecycle.Released;
        }
        return ContainerFieldLifecycle.Leaked;
    }

    private static void ReportLeak(SyntaxNodeAnalysisContext ctx, IFieldSymbol field) {
        var fieldDecl = field.DeclaringSyntaxReferences
            .FirstOrDefault()?.GetSyntax(ctx.CancellationToken) as VariableDeclaratorSyntax;
        if (fieldDecl is not null)
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, fieldDecl.Identifier.GetLocation(), field.Name));
    }

    private List<IFieldSymbol> CollectTargetFields(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        var result = new List<IFieldSymbol>();
        foreach (var member in typeSymbol.GetMembers()) {
            if (member is not IFieldSymbol field) continue;
            if (field.IsStatic) continue;
            if (IsTargetField(field, idisposable, iasyncDisposable))
                result.Add(field);
        }
        return result;
    }

    private static List<IMethodSymbol> GetDisposeMethods(INamedTypeSymbol typeSymbol) {
        var result = new List<IMethodSymbol>();
        foreach (var member in typeSymbol.GetMembers()) {
            if (member is not IMethodSymbol method) continue;
            if (DisposeMethodNames.Contains(method.Name))
                result.Add(method);
        }
        return result;
    }

    // ============================================================
    // 共享:BodyReleasesField 框架 — fieldBackedLocals 收集 + detector 检查 + 调用链递归
    // ============================================================

    /// <summary>
    /// 检查 Dispose 方法体是否释放了该字段。
    /// 1. 收集 field-backed locals(局部变量/参数被赋值为字段引用)
    /// 2. 用 detector 检查当前方法体中的释放模式
    /// 3. 递归追踪调用链(辅助方法体内同样检测)
    /// </summary>
    protected static bool BodyReleasesField(
        BlockSyntax body,
        IFieldSymbol field,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct,
        HashSet<ISymbol> visited,
        ReleaseDetector detector) {
        var fieldBackedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        CollectFieldBackedLocals(body, field, semanticModel, ct, fieldBackedLocals);

        if (detector(body, field, fieldBackedLocals, semanticModel, ct)) return true;

        return TraceCallChain(body, field, semanticModel, compilation, ct, visited, detector);
    }

    private static bool TraceCallChain(
        BlockSyntax body,
        IFieldSymbol field,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct,
        HashSet<ISymbol> visited,
        ReleaseDetector detector) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return false;
            var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
            if (methodSymbol is null) continue;
            if (!visited.Add(methodSymbol)) continue;
            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) continue;
            var methodDecl = syntaxRef.GetSyntax(ct) as MethodDeclarationSyntax;
            if (methodDecl?.Body is null) continue;
            var helperModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);
            if (helperModel is null) continue;
            if (BodyReleasesField(methodDecl.Body, field, helperModel, compilation, ct, visited, detector))
                return true;
        }
        return false;
    }

    /// <summary>收集 field-backed locals:局部变量初始化/赋值/模式匹配为字段引用</summary>
    protected static void CollectFieldBackedLocals(
        BlockSyntax body,
        IFieldSymbol field,
        SemanticModel semanticModel,
        CancellationToken ct,
        HashSet<ISymbol> fieldBackedLocals) {
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
        foreach (var isPattern in body.DescendantNodes().OfType<IsPatternExpressionSyntax>()) {
            if (!ReferencesField(isPattern.Expression, field, semanticModel, ct)) continue;
            foreach (var designation in isPattern.Pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>()) {
                var local = semanticModel.GetDeclaredSymbol(designation, ct);
                if (local is not null) fieldBackedLocals.Add(local);
            }
        }
    }

    // ============================================================
    // 共享:字段引用判定
    // ============================================================

    protected static bool ReferencesField(ExpressionSyntax expr, IFieldSymbol field, SemanticModel semanticModel, CancellationToken ct) {
        foreach (var node in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
        }
        return false;
    }

    protected static bool ReferencesFieldOrLocal(ExpressionSyntax expr, IFieldSymbol field, HashSet<ISymbol> fieldBackedLocals, SemanticModel semanticModel, CancellationToken ct) {
        foreach (var node in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
            if (symbol is not null && fieldBackedLocals.Contains(symbol)) return true;
        }
        return false;
    }

    /// <summary>精确判定表达式本身是否是字段/local 引用(不检查后代,避免误判传参)</summary>
    protected static bool IsFieldOrLocalReference(ExpressionSyntax expr, IFieldSymbol field, HashSet<ISymbol> fieldBackedLocals, SemanticModel semanticModel, CancellationToken ct) {
        var symbol = semanticModel.GetSymbolInfo(expr, ct).Symbol;
        if (SymbolEqualityComparer.Default.Equals(symbol, field)) return true;
        if (symbol is not null && fieldBackedLocals.Contains(symbol)) return true;
        return false;
    }

    protected static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface) {
        if (SymbolEqualityComparer.Default.Equals(type, iface)) return true;
        foreach (var i in type.AllInterfaces) {
            if (SymbolEqualityComparer.Default.Equals(i, iface)) return true;
        }
        return false;
    }

    /// <summary>Task.WhenAll(field) 或 Task.WhenAll(snapshot) 调用检测</summary>
    protected static bool IsWhenAllOnField(
        InvocationExpressionSyntax invocation,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
        if (methodSymbol is null) return false;
        if (methodSymbol.Name != "WhenAll" || methodSymbol.ContainingType?.Name != "Task") return false;
        if (invocation.ArgumentList.Arguments.Count == 0) return false;
        return ReferencesFieldOrLocal(invocation.ArgumentList.Arguments[0].Expression, field, fieldBackedLocals, semanticModel, ct);
    }
}
