namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9301/JCC9302: 拥有字段未释放 / 可空拥有字段释放后未置 null。
/// 状态机驱动:Unknown → Owned/Borrowed/Skip → Released/Leaked。
/// 拥有判定:new/ImplicitNew/工厂返回 IDisposable;借用判定:构造函数参数赋值。
/// 释放检测:Dispose 调用链中引用字段(CheckInDisposeCallChain + MethodBodyReferencesField)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9301",
    Title = "内存泄漏: 拥有字段未释放",
    Description = "通过 new 或工厂创建的 IDisposable 字段 '{0}' 未在 Dispose/DisposeAsync 中释放。拥有字段未释放将导致资源泄漏。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "通过 new 或工厂创建的 IDisposable 字段必须在 Dispose/DisposeAsync 中释放。正确做法: " +
        "1) public void Dispose() { _field.Dispose(); } / public async ValueTask DisposeAsync() { await _field.DisposeAsync().ConfigureAwait(false); } " +
        "2) 若字段是借用(构造函数注入),由 DI 容器或调用方管理生命周期,本规则不报。",
    IsCompilationEnd = false)]
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9302",
    Title = "内存泄漏: 可空拥有字段释放后未置null",
    Description = "可空拥有字段 '{0}' 在 Dispose/DisposeAsync 中释放后未置 null。置null可防止use-after-free并明确表达释放意图。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "可空拥有字段在 Dispose 中释放后应置 null。正确做法: field?.Dispose(); field = null; " +
        "置null可: 1) 防止use-after-free; 2) 明确表达释放意图; 3) 帮助检测重复释放。",
    IsCompilationEnd = false)]
public sealed class DisposableFieldReleaseRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map =
        RuleDescriptorFactory.CreateAll<DisposableFieldReleaseRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    /// <summary>
    /// 字段所有权状态机 — 驱动分析流程
    /// </summary>
    private enum Ownership {
        /// <summary>未分析:无赋值信息</summary>
        Unknown,
        /// <summary>拥有:new/工厂创建,当前类型负责释放</summary>
        Owned,
        /// <summary>借用:构造函数注入,DI 容器或调用方管理生命周期</summary>
        Borrowed,
        /// <summary>跳过:null 初始化/无法判定</summary>
        Skip,
    }

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var idisposable = context.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposable = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeDeclaration(ctx, idisposable, iasyncDisposable),
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
        if (!IsDisposableType(typeSymbol, idisposable, iasyncDisposable)) return;

        if (IsNotFirstPartialDeclaration(typeSymbol, typeDecl, ctx.CancellationToken)) return;

        var compilation = ctx.SemanticModel.Compilation;
        var allMembers = typeSymbol.GetMembers();

        var disposableFields = CollectDisposableFields(allMembers, idisposable, iasyncDisposable);
        if (disposableFields.Count == 0) return;

        foreach (var field in disposableFields) {
            AnalyzeField(field, typeSymbol, allMembers, compilation, ctx);
        }
    }

    private static void AnalyzeField(
        IFieldSymbol field,
        INamedTypeSymbol typeSymbol,
        System.Collections.Immutable.ImmutableArray<ISymbol> allMembers,
        Compilation compilation,
        SyntaxNodeAnalysisContext ctx) {
        var ownership = DetermineOwnership(field, allMembers, compilation, ctx.CancellationToken);
        if (ownership != Ownership.Owned) return;

        var released = AotSafetyHelpers.CheckInDisposeCallChain(typeSymbol,
            m => AotSafetyHelpers.MethodBodyReferencesField(m, field.Name));

        var location = GetFieldLocation(field, ctx.CancellationToken);
        if (location is null) return;

        if (!released) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9301"], location, field.Name));
        } else if (field.NullableAnnotation == NullableAnnotation.Annotated && !field.IsReadOnly) {
            var nulled = AotSafetyHelpers.CheckInDisposeCallChain(typeSymbol,
                m => AotSafetyHelpers.MethodBodyNullsField(m, field.Name));
            if (!nulled) {
                ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9302"], location, field.Name));
            }
        }
    }

    private static List<IFieldSymbol> CollectDisposableFields(
        System.Collections.Immutable.ImmutableArray<ISymbol> members,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        var result = new List<IFieldSymbol>();
        foreach (var member in members) {
            if (member is not IFieldSymbol field) continue;
            if (field.IsStatic) continue;
            if (field.IsImplicitlyDeclared) continue;
            if (!IsDisposableType(field.Type, idisposable, iasyncDisposable)) continue;
            result.Add(field);
        }
        return result;
    }

    /// <summary>
    /// 状态机阶段:判定字段所有权
    /// Unknown → (分析赋值源) → Owned | Borrowed | Skip
    /// </summary>
    private static Ownership DetermineOwnership(
        IFieldSymbol field,
        System.Collections.Immutable.ImmutableArray<ISymbol> allMembers,
        Compilation compilation,
        CancellationToken ct) {
        var fromInitializer = ClassifyInitializer(field, compilation, ct);
        if (fromInitializer == Ownership.Owned) return Ownership.Owned;
        if (fromInitializer == Ownership.Skip) return Ownership.Skip;

        var (hasOwned, hasBorrowed) = ClassifyMethodAssignments(field, allMembers, compilation, ct);
        if (hasOwned) return Ownership.Owned;
        if (hasBorrowed) return Ownership.Borrowed;
        if (fromInitializer == Ownership.Borrowed) return Ownership.Borrowed;
        return Ownership.Skip;
    }

    private static Ownership ClassifyInitializer(IFieldSymbol field, Compilation compilation, CancellationToken ct) {
        if (field.DeclaringSyntaxReferences.Length == 0) return Ownership.Unknown;
        var fieldDecl = field.DeclaringSyntaxReferences[0].GetSyntax(ct) as VariableDeclaratorSyntax;
        if (fieldDecl?.Initializer?.Value is not { } initValue) return Ownership.Unknown;
        var fieldModel = compilation.GetSemanticModel(fieldDecl.SyntaxTree);
        if (fieldModel is null) return Ownership.Unknown;
        return ClassifyExpression(initValue, fieldModel, ct);
    }

    private static (bool hasOwned, bool hasBorrowed) ClassifyMethodAssignments(
        IFieldSymbol field,
        System.Collections.Immutable.ImmutableArray<ISymbol> allMembers,
        Compilation compilation,
        CancellationToken ct) {
        var hasOwned = false;
        var hasBorrowed = false;
        foreach (var member in allMembers) {
            if (member is not IMethodSymbol method) continue;
            var methodNode = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
            if (methodNode is null) continue;
            var methodModel = compilation.GetSemanticModel(methodNode.SyntaxTree);
            if (methodModel is null) continue;
            var isConstructor = method.MethodKind == MethodKind.Constructor;

            foreach (var assign in methodNode.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>()) {
                var leftSymbol = methodModel.GetSymbolInfo(assign.Left, ct).Symbol;
                if (!SymbolEqualityComparer.Default.Equals(leftSymbol, field)) continue;
                var ownership = ClassifyAssignment(assign.Right, methodModel, ct, isConstructor);
                if (ownership == Ownership.Owned) hasOwned = true;
                if (ownership == Ownership.Borrowed) hasBorrowed = true;
            }
        }
        return (hasOwned, hasBorrowed);
    }

    private static Ownership ClassifyAssignment(
        ExpressionSyntax right,
        SemanticModel semanticModel,
        CancellationToken ct,
        bool isConstructor) {
        var exprOwnership = ClassifyExpression(right, semanticModel, ct);
        if (exprOwnership != Ownership.Unknown) return exprOwnership;
        if (isConstructor && IsParameterReference(right, semanticModel, ct))
            return Ownership.Borrowed;
        return Ownership.Unknown;
    }

    private static Ownership ClassifyExpression(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (AotSafetyHelpers.IsNullLiteral(expr)) return Ownership.Skip;
        var unwrapped = UnwrapAwait(expr);
        if (unwrapped is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
            return Ownership.Owned;
        if (unwrapped is InvocationExpressionSyntax invocation) {
            if (IsDiContainerResolve(invocation, semanticModel, ct))
                return Ownership.Borrowed;
            if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method
                && ReturnsDisposable(method))
                return Ownership.Owned;
        }
        return Ownership.Unknown;
    }

    /// <summary>
    /// 判定是否为 DI 容器解析调用(GetService/GetRequiredService/GetKeyedService/GetRequiredKeyedService)。
    /// DI 容器返回的对象由容器管理生命周期,调用方是借用而非拥有。
    /// </summary>
    private static bool IsDiContainerResolve(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken ct) {
        var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
        if (methodSymbol is null) return false;
        return methodSymbol.Name is
            "GetService" or
            "GetRequiredService" or
            "GetKeyedService" or
            "GetRequiredKeyedService";
    }

    private static ExpressionSyntax UnwrapAwait(ExpressionSyntax expr) {
        while (expr is AwaitExpressionSyntax awaitExpr)
            expr = awaitExpr.Expression;
        return expr;
    }

    private static bool IsParameterReference(ExpressionSyntax expr, SemanticModel semanticModel, CancellationToken ct) {
        var unwrapped = UnwrapAwait(expr);
        return semanticModel.GetSymbolInfo(unwrapped, ct).Symbol is IParameterSymbol;
    }

    private static bool ReturnsDisposable(IMethodSymbol method)
        => method.ReturnType is INamedTypeSymbol returnType && ImplementsAnyDisposable(returnType);

    private static bool ImplementsAnyDisposable(INamedTypeSymbol type) {
        if (type.Name is "IDisposable" or "IAsyncDisposable") return true;
        foreach (var iface in type.AllInterfaces) {
            if (iface.Name is "IDisposable" or "IAsyncDisposable") return true;
        }
        return false;
    }

    private static Location? GetFieldLocation(IFieldSymbol field, CancellationToken ct) {
        var fieldDecl = field.DeclaringSyntaxReferences
            .FirstOrDefault()?.GetSyntax(ct) as VariableDeclaratorSyntax;
        return fieldDecl?.Identifier.GetLocation();
    }

    private static bool IsNotFirstPartialDeclaration(
        INamedTypeSymbol typeSymbol,
        TypeDeclarationSyntax typeDecl,
        CancellationToken ct) {
        if (typeSymbol.DeclaringSyntaxReferences.Length <= 1) return false;
        var firstDecl = typeSymbol.DeclaringSyntaxReferences[0].GetSyntax(ct);
        return firstDecl != typeDecl;
    }

    private static bool IsDisposableType(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
        if (type is not INamedTypeSymbol named) return false;
        if (named.IsValueType) return false;
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
}
