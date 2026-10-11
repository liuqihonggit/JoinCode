namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9307: Task 遗忘等待 — 类型持有 Task/ValueTask 元素的集合字段或单个 Task/ValueTask 字段,
/// 但 Dispose/DisposeAsync 未 await Task.WhenAll 等待集合或未 await 等待单字段完成。
/// 后台任务可能持有资源,未等待完成即释放会导致资源泄露。
/// 识别集合: List<T>/IList<T>/ICollection<T>/IEnumerable<T>/Dictionary<K,V>/ConcurrentDictionary<K,V>/T[] 等(T 是 Task/ValueTask)。
/// 识别单字段: Task/ValueTask 类型的实例字段。
/// 完备释放识别: Dispose 体含 await Task.WhenAll(field) 或 await field.ConfigureAwait(false)。
/// 状态机:Unknown → Owned(Task 集合/单字段)→ Released/Leaked。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9307",
    Title = "内存泄漏: Task 遗忘等待",
    Description = "类型持有 Task 字段 '{0}',但 Dispose/DisposeAsync 未 await Task.WhenAll 等待集合或未 await 等待单字段完成。后台任务可能持有资源,未等待完成即释放会导致资源泄露。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "Task 字段必须在 Dispose/DisposeAsync 中 await 等待完成,确保后台任务持有的资源释放闭合。正确做法: " +
        "1) 集合: await Task.WhenAll(_tasks).ConfigureAwait(false); 或 await Task.WhenAll(_tasks.Values).ConfigureAwait(false); " +
        "2) 单字段: await _bgLoop.ConfigureAwait(false); " +
        "3) 若任务已保证完成(如通过 CancellationToken + 等待 Consumer 退出),在 PostStopAsync 中 WhenAll 残留任务。",
    IsCompilationEnd = false)]
public sealed class ContainerTaskReleaseRule : ContainerReleaseRuleBase<ContainerTaskReleaseRule> {
    protected override bool IsTargetField(IFieldSymbol field, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable)
        => IsTaskField(field.Type);

    protected override ReleaseDetector CreateReleaseDetector() => DetectTaskRelease;

    /// <summary>判定字段类型是否为 Task 集合或单个 Task/ValueTask</summary>
    private static bool IsTaskField(ITypeSymbol type) {
        if (IsTaskType(type)) return true;
        return IsTaskElementContainer(type);
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

    /// <summary>释放检测:Task.WhenAll(集合) + await field(单字段) + return field(辅助方法包装)</summary>
    private static bool DetectTaskRelease(
        BlockSyntax body,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (IsWhenAllOnField(invocation, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var awaitExpr in body.DescendantNodes().OfType<AwaitExpressionSyntax>()) {
            if (IsAwaitOnField(awaitExpr, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var returnStmt in body.DescendantNodes().OfType<ReturnStatementSyntax>()) {
            if (returnStmt.Expression is not null
                && ReferencesFieldOrLocal(returnStmt.Expression, field, fieldBackedLocals, semanticModel, ct))
                return true;
        }
        return false;
    }

    /// <summary>await field / await snapshot / await field.Method(...) / await field.Method1().Method2() 等</summary>
    private static bool IsAwaitOnField(
        AwaitExpressionSyntax awaitExpr,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct) {
        return IsExpressionOnField(awaitExpr.Expression, field, fieldBackedLocals, semanticModel, ct);
    }

    /// <summary>递归剥开 MemberAccess 接收者,检查表达式是否最终基于 field/local 引用</summary>
    private static bool IsExpressionOnField(
        ExpressionSyntax expr,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (IsFieldOrLocalReference(expr, field, fieldBackedLocals, semanticModel, ct)) return true;

        if (expr is InvocationExpressionSyntax inv
            && inv.Expression is MemberAccessExpressionSyntax ma)
            return IsExpressionOnField(ma.Expression, field, fieldBackedLocals, semanticModel, ct);

        return false;
    }
}
