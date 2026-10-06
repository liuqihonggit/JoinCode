namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9306: 容器释放不完备 — 类型持有 IDisposable/IAsyncDisposable 元素的集合字段,
/// 但 Dispose/DisposeAsync 未遍历释放所有元素。容器 Dispose 漏掉任一元素将导致资源泄漏。
/// 识别集合: List<T>/IList<T>/ICollection<T>/IEnumerable<T>/Dictionary<K,V>/ConcurrentDictionary<K,V>/T[] 等。
/// 完备释放识别: Dispose 体含 foreach(x in field) x.Dispose() 或 await Task.WhenAll(field)。
/// 状态机:Unknown → Owned(容器持有 Disposable 元素)→ Released/Leaked。
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
public sealed class ContainerCollectionReleaseRule : ContainerReleaseRuleBase<ContainerCollectionReleaseRule> {
    protected override bool IsTargetField(IFieldSymbol field, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable)
        => IsDisposableElementContainer(field.Type, idisposable, iasyncDisposable);

    protected override ReleaseDetector CreateReleaseDetector() => DetectCollectionRelease;

    /// <summary>释放检测:foreach 遍历字段释放 + Task.WhenAll(字段)</summary>
    private static bool DetectCollectionRelease(
        BlockSyntax body,
        IFieldSymbol field,
        HashSet<ISymbol> fieldBackedLocals,
        SemanticModel semanticModel,
        CancellationToken ct) {
        foreach (var foreachStmt in body.DescendantNodes().OfType<ForEachStatementSyntax>()) {
            if (ReferencesFieldOrLocal(foreachStmt.Expression, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var foreachStmt in body.DescendantNodes().OfType<ForEachVariableStatementSyntax>()) {
            if (ReferencesFieldOrLocal(foreachStmt.Expression, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (IsWhenAllOnField(invocation, field, fieldBackedLocals, semanticModel, ct)) return true;
        }
        return false;
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
}
