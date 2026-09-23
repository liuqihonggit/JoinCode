using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JccAuditCli;

/// <summary>
/// IDisposable/IAsyncDisposable 一致性检测器
/// 规则：
///   JCC9102 (Error): 类型同时实现 IDisposable 和 IAsyncDisposable — 应二选一
///   JCC9103 (Warning): 类型实现 IAsyncDisposable 但 DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using
///   JCC9004 (Warning): 类型实现 IAsyncDisposable 但消费方用 using（非 await using）— 应改用 await using
/// </summary>
public static class DisposableConsistencyChecker {
    private static readonly string IDisposableFullName = typeof(IDisposable).FullName!;
    private static readonly string IAsyncDisposableFullName = typeof(IAsyncDisposable).FullName!;

    /// <summary>从编译中提取 IDisposable/IAsyncDisposable 一致性问题。</summary>
    /// <param name="compilation">Roslyn 编译。</param>
    /// <returns>检测结果列表。</returns>
    public static List<DisposableConsistencyInfo> Extract(Compilation compilation) {
        var results = new List<DisposableConsistencyInfo>();

        var idisposableType = compilation.GetTypeByMetadataName(IDisposableFullName);
        var iasyncDisposableType = compilation.GetTypeByMetadataName(IAsyncDisposableFullName);

        if (idisposableType is null || iasyncDisposableType is null)
            return results;

        foreach (var syntaxTree in compilation.SyntaxTrees) {
            var filePath = syntaxTree.FilePath ?? string.Empty;
            if (filePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                filePath.Contains("/obj/", StringComparison.Ordinal))
                continue;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            ExtractFromTree(syntaxTree, semanticModel, filePath, idisposableType, iasyncDisposableType, results);
        }

        return results;
    }

    private static void ExtractFromTree(SyntaxTree syntaxTree, SemanticModel semanticModel,
        string filePath, INamedTypeSymbol idisposableType, INamedTypeSymbol iasyncDisposableType,
        List<DisposableConsistencyInfo> results) {
        var root = syntaxTree.GetRoot();

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>()) {
            var symbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            if (symbol is null) continue;

            var implementsIDisposable = symbol.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
            var implementsIAsyncDisposable = symbol.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);

            if (implementsIDisposable && implementsIAsyncDisposable) {
                var hasRealAsyncDispose = HasRealAsyncDispose(symbol);

                results.Add(new DisposableConsistencyInfo {
                    TypeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    FilePath = filePath,
                    Line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    RuleId = hasRealAsyncDispose ? "JCC9102" : "JCC9103",
                    Severity = hasRealAsyncDispose ? "Error" : "Warning",
                    Message = hasRealAsyncDispose
                        ? $"同时实现 IDisposable 和 IAsyncDisposable — 应二选一。项目统一模式：接口层用 IAsyncDisposable，消费方用 await using"
                        : $"同时实现 IDisposable 和 IAsyncDisposable，但 DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable"
                });
            }
        }
    }

    /// <summary>
    /// 检查 DisposeAsync() 是否有真正的异步逻辑（而非仅委托给 Dispose()）
    /// </summary>
    private static bool HasRealAsyncDispose(INamedTypeSymbol type) {
        foreach (var member in type.GetMembers("DisposeAsync")) {
            if (member is not IMethodSymbol method) continue;

            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) continue;

            var node = syntaxRef.GetSyntax();
            var block = node.DescendantNodes().OfType<BlockSyntax>().FirstOrDefault();
            if (block is null) continue;

            var bodyText = block.ToString();

            var isTrivialDelegate =
                bodyText.Contains("Dispose();") &&
                bodyText.Contains("ValueTask.CompletedTask") &&
                !bodyText.Contains("await ");

            if (!isTrivialDelegate)
                return true;
        }

        return false;
    }
}

/// <summary>
/// IDisposable/IAsyncDisposable 一致性检测结果
/// </summary>
public sealed record DisposableConsistencyInfo {
    /// <summary>获取类型名称。</summary>
    public string TypeName { get; init; } = string.Empty;
    /// <summary>获取文件路径。</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>获取行号。</summary>
    public int Line { get; init; }
    /// <summary>获取规则 ID。</summary>
    public string RuleId { get; init; } = string.Empty;
    /// <summary>获取严重级别。</summary>
    public string Severity { get; init; } = string.Empty;
    /// <summary>获取检测消息。</summary>
    public string Message { get; init; } = string.Empty;
}