using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JccAuditCli;

/// <summary>
/// JCC9103 批量修复器 — 从同时实现 IDisposable+IAsyncDisposable 的类型中移除 IDisposable
/// 保留 Dispose() 方法（变为普通方法），保留 DisposeAsync() 不变
/// </summary>
public static class DualDisposableFixer {
    /// <summary>
    /// 批量修复 JCC9103 违规：从类型基列表中移除 IDisposable
    /// </summary>
    /// <param name="filePaths">待修复文件路径列表</param>
    /// <param name="dryRun">仅模拟不写入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>修复报告</returns>
    public static async Task<DualDisposableFixReport> FixAsync(IReadOnlyList<string> filePaths, bool dryRun, CancellationToken ct) {
        var affectedFiles = new ConcurrentBag<string>();
        var interfaceRemovals = 0;
        var totalChanges = 0;
        var semaphore = new SemaphoreSlim(8);
        var processedCount = 0;

        var tasks = filePaths.Select(async filePath => {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (!File.Exists(filePath)) return;
                var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var rewriter = new DualDisposableRewriter();
                var newRoot = rewriter.Visit(root);

                if (rewriter.FixedCount <= 0) return;

                affectedFiles.Add(filePath);
                Interlocked.Add(ref interfaceRemovals, rewriter.InterfaceRemovalCount);
                Interlocked.Add(ref totalChanges, rewriter.FixedCount);

                if (!dryRun) {
                    var newText = newRoot.ToFullString();
                    await File.WriteAllTextAsync(filePath, newText, ct).ConfigureAwait(false);
                }

                var count = Interlocked.Increment(ref processedCount);
                if (count % 50 == 0)
                    Console.WriteLine($"  [修复 {count}/{filePaths.Count}]...");
            } catch (Exception ex) {
                Console.Error.WriteLine($"  修复失败: {Path.GetFileName(filePath)} - {ex.Message}");
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        semaphore.Dispose();

        return new DualDisposableFixReport {
            AffectedFiles = affectedFiles.ToList(),
            InterfaceRemovals = interfaceRemovals,
            TotalChanges = totalChanges,
        };
    }
}

/// <summary>JCC9103 批量修复报告</summary>
public sealed class DualDisposableFixReport {
    /// <summary>受影响文件列表</summary>
    public List<string> AffectedFiles { get; init; } = [];
    /// <summary>接口移除数</summary>
    public int InterfaceRemovals { get; init; }
    /// <summary>总变更数</summary>
    public int TotalChanges { get; init; }

    /// <summary>打印报告摘要</summary>
    public void PrintSummary() {
        Console.WriteLine();
        Console.WriteLine("=== JCC9103 批量修复报告 ===");
        Console.WriteLine($"受影响文件: {AffectedFiles.Count}");
        Console.WriteLine($"  接口移除数: {InterfaceRemovals}");
        Console.WriteLine($"  总变更数:   {TotalChanges}");
    }
}

/// <summary>
/// SyntaxRewriter — 从类型基列表中移除 IDisposable（当类型同时实现 IDisposable+IAsyncDisposable）
/// </summary>
internal class DualDisposableRewriter : CSharpSyntaxRewriter {
    /// <summary>总修复数</summary>
    public int FixedCount { get; private set; }
    /// <summary>接口移除数</summary>
    public int InterfaceRemovalCount { get; private set; }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) {
        var newNode = base.VisitClassDeclaration(node);
        return ProcessTypeDeclaration(node, newNode as ClassDeclarationSyntax, (n, bl) => n.WithBaseList(bl));
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) {
        var newNode = base.VisitStructDeclaration(node);
        return ProcessTypeDeclaration(node, newNode as StructDeclarationSyntax, (n, bl) => n.WithBaseList(bl));
    }

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) {
        var newNode = base.VisitRecordDeclaration(node);
        return ProcessTypeDeclaration(node, newNode as RecordDeclarationSyntax, (n, bl) => n.WithBaseList(bl));
    }

    private T? ProcessTypeDeclaration<T>(T node, T? visitedNode, Func<T, BaseListSyntax?, T> withBaseList) where T : TypeDeclarationSyntax {
        var current = visitedNode ?? node;
        var baseList = current.BaseList;
        if (baseList is null) return current;

        var hasIDisposable = false;
        var hasIAsyncDisposable = false;
        foreach (var baseType in baseList.Types) {
            var typeName = baseType.Type.ToString();
            if (typeName == "IDisposable") hasIDisposable = true;
            if (typeName == "IAsyncDisposable") hasIAsyncDisposable = true;
        }

        if (!hasIDisposable || !hasIAsyncDisposable) return current;

        var newTypes = new List<BaseTypeSyntax>();
        foreach (var baseType in baseList.Types) {
            var typeName = baseType.Type.ToString();
            if (typeName == "IDisposable") {
                InterfaceRemovalCount++;
                FixedCount++;
                continue;
            }
            newTypes.Add(baseType);
        }

        var newBaseList = newTypes.Count > 0
            ? baseList.WithTypes(SyntaxFactory.SeparatedList(newTypes))
            : null;

        return withBaseList(current, newBaseList);
    }
}
