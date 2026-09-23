using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace JccAuditCli;

/// <summary>
/// Disposable 方向转换器 — IDisposable ↔ IAsyncDisposable 双向改动
/// 方向: Async = IDisposable→IAsyncDisposable, Sync = IAsyncDisposable→IDisposable
/// </summary>
public static class DisposableDirectionFixer {
    /// <summary>
    /// 执行方向转换，返回传播范围统计
    /// </summary>
    public static async Task<DirectionFixReport> FixAsync(string solutionPath, string targetTypeName, FixDirection direction, bool dryRun, CancellationToken ct) {
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);

        var report = new DirectionFixReport { Direction = direction, TargetTypeName = targetTypeName };

        foreach (var project in solution.Projects) {
            if (ct.IsCancellationRequested) break;

            foreach (var document in project.Documents) {
                if (ct.IsCancellationRequested) break;

                var filePath = document.FilePath;
                if (filePath is null) continue;
                if (ShouldSkipFile(filePath)) continue;

                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var isTestFile = IsTestFile(filePath);
                var rewriter = new DisposableDirectionRewriter(direction, targetTypeName, isTestFile, filePath);
                var newRoot = rewriter.Visit(root);

                if (newRoot != root && rewriter.FixedCount > 0) {
                    report.AffectedFiles.Add(filePath);
                    report.InterfaceChanges += rewriter.InterfaceChangeCount;
                    report.MethodSignatureChanges += rewriter.MethodSignatureChangeCount;
                    report.CallSiteChanges += rewriter.CallSiteChangeCount;
                    report.UsingDeclChanges += rewriter.UsingDeclChangeCount;
                    report.TotalChanges += rewriter.FixedCount;

                    if (!dryRun) {
                        var newDoc = document.WithSyntaxRoot(newRoot);
                        var text = await newDoc.GetTextAsync(ct).ConfigureAwait(false);
                        await File.WriteAllTextAsync(filePath, text.ToString(), ct).ConfigureAwait(false);
                    }

                    var tag = dryRun ? "[DRY-RUN]" : "[FIXED]";
                    Console.WriteLine($"  {tag} {Path.GetFileName(filePath)}: 接口{rewriter.InterfaceChangeCount} 签名{rewriter.MethodSignatureChangeCount} 调用{rewriter.CallSiteChangeCount} using{rewriter.UsingDeclChangeCount}");
                }
            }
        }

        return report;
    }

    private static bool ShouldSkipFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        return false;
    }

    private static bool IsTestFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains(".tests/") || normalized.Contains("/test/");
    }
}

/// <summary>转换方向</summary>
public enum FixDirection {
    /// <summary>IDisposable → IAsyncDisposable</summary>
    Async,
    /// <summary>IAsyncDisposable → IDisposable</summary>
    Sync
}

/// <summary>方向转换报告</summary>
public sealed class DirectionFixReport {
    /// <summary>转换方向</summary>
    public FixDirection Direction { get; init; }
    /// <summary>目标类型名</summary>
    public string TargetTypeName { get; init; } = string.Empty;
    /// <summary>受影响文件列表</summary>
    public List<string> AffectedFiles { get; } = [];
    /// <summary>接口声明变更数</summary>
    public int InterfaceChanges { get; set; }
    /// <summary>方法签名变更数</summary>
    public int MethodSignatureChanges { get; set; }
    /// <summary>调用点变更数</summary>
    public int CallSiteChanges { get; set; }
    /// <summary>using 声明变更数</summary>
    public int UsingDeclChanges { get; set; }
    /// <summary>总变更数</summary>
    public int TotalChanges { get; set; }

    /// <summary>打印报告摘要</summary>
    public void PrintSummary() {
        var dirName = Direction == FixDirection.Async ? "异步化 (IDisposable→IAsyncDisposable)" : "同步化 (IAsyncDisposable→IDisposable)";
        Console.WriteLine();
        Console.WriteLine($"=== {dirName} 报告 ===");
        Console.WriteLine($"目标类型: {TargetTypeName}");
        Console.WriteLine($"受影响文件: {AffectedFiles.Count}");
        Console.WriteLine($"  接口声明变更: {InterfaceChanges}");
        Console.WriteLine($"  方法签名变更: {MethodSignatureChanges}");
        Console.WriteLine($"  调用点变更:   {CallSiteChanges}");
        Console.WriteLine($"  using 声明变更: {UsingDeclChanges}");
        Console.WriteLine($"  总变更数:     {TotalChanges}");
    }
}

/// <summary>
/// SyntaxRewriter — 双向改写 IDisposable ↔ IAsyncDisposable
/// </summary>
internal class DisposableDirectionRewriter : CSharpSyntaxRewriter {
    private readonly FixDirection _direction;
    private readonly string _targetTypeName;
    private readonly bool _isTestFile;
    private readonly string _filePath;

    /// <summary>总修复数</summary>
    public int FixedCount { get; private set; }
    /// <summary>接口声明变更数</summary>
    public int InterfaceChangeCount { get; private set; }
    /// <summary>方法签名变更数</summary>
    public int MethodSignatureChangeCount { get; private set; }
    /// <summary>调用点变更数</summary>
    public int CallSiteChangeCount { get; private set; }
    /// <summary>using 声明变更数</summary>
    public int UsingDeclChangeCount { get; private set; }

    internal DisposableDirectionRewriter(FixDirection direction, string targetTypeName, bool isTestFile, string filePath) {
        _direction = direction;
        _targetTypeName = targetTypeName;
        _isTestFile = isTestFile;
        _filePath = filePath;
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) {
        var changed = false;

        // 1. 改接口声明
        var newBaseList = VisitBaseList(node.BaseList, ref changed);

        // 2. 改方法签名 (Dispose ↔ DisposeAsync)
        var newMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in node.Members) {
            if (_direction == FixDirection.Async && IsSyncDisposeMethod(member, out var syncMethod)) {
                newMembers.Add(ConvertDisposeToDisposeAsync(syncMethod));
                MethodSignatureChangeCount++;
                FixedCount++;
                changed = true;
            } else if (_direction == FixDirection.Sync && IsAsyncDisposeMethod(member, out var asyncMethod)) {
                newMembers.Add(ConvertDisposeAsyncToDispose(asyncMethod));
                MethodSignatureChangeCount++;
                FixedCount++;
                changed = true;
            } else {
                newMembers.Add(member);
            }
        }

        if (!changed)
            return base.VisitClassDeclaration(node);

        if (newBaseList is not null)
            node = node.WithBaseList(newBaseList);
        node = node.WithMembers(SyntaxFactory.List(newMembers));

        InterfaceChangeCount += _interfaceChangesThisClass;
        _interfaceChangesThisClass = 0;

        return base.VisitClassDeclaration(node);
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) {
        var changed = false;
        var newBaseList = VisitBaseList(node.BaseList, ref changed);

        var newMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in node.Members) {
            if (_direction == FixDirection.Async && IsSyncDisposeMethod(member, out var syncMethod)) {
                newMembers.Add(ConvertDisposeToDisposeAsync(syncMethod));
                MethodSignatureChangeCount++;
                FixedCount++;
                changed = true;
            } else if (_direction == FixDirection.Sync && IsAsyncDisposeMethod(member, out var asyncMethod)) {
                newMembers.Add(ConvertDisposeAsyncToDispose(asyncMethod));
                MethodSignatureChangeCount++;
                FixedCount++;
                changed = true;
            } else {
                newMembers.Add(member);
            }
        }

        if (!changed)
            return base.VisitStructDeclaration(node);

        if (newBaseList is not null)
            node = node.WithBaseList(newBaseList);
        node = node.WithMembers(SyntaxFactory.List(newMembers));

        InterfaceChangeCount += _interfaceChangesThisClass;
        _interfaceChangesThisClass = 0;

        return base.VisitStructDeclaration(node);
    }

    private int _interfaceChangesThisClass;

    private BaseListSyntax? VisitBaseList(BaseListSyntax? baseList, ref bool changed) {
        if (baseList is null) return null;

        var newTypes = new List<BaseTypeSyntax>();
        foreach (var baseType in baseList.Types) {
            var typeName = baseType.Type.ToString();

            if (_direction == FixDirection.Async) {
                // IDisposable → IAsyncDisposable
                if (typeName == "IDisposable") {
                    newTypes.Add(SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.IdentifierName("IAsyncDisposable")).WithTriviaFrom(baseType));
                    _interfaceChangesThisClass++;
                    FixedCount++;
                    changed = true;
                    continue;
                }
                // 如果同时有 IDisposable 和 IAsyncDisposable，删 IDisposable
                if (typeName == "IDisposable" && HasIAsyncDisposable(baseList)) {
                    _interfaceChangesThisClass++;
                    FixedCount++;
                    changed = true;
                    continue;
                }
            } else {
                // IAsyncDisposable → IDisposable
                if (typeName == "IAsyncDisposable") {
                    newTypes.Add(SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.IdentifierName("IDisposable")).WithTriviaFrom(baseType));
                    _interfaceChangesThisClass++;
                    FixedCount++;
                    changed = true;
                    continue;
                }
            }

            newTypes.Add(baseType);
        }

        if (!changed) return baseList;
        return baseList.WithTypes(SyntaxFactory.SeparatedList(newTypes));
    }

    private static bool HasIAsyncDisposable(BaseListSyntax baseList) {
        foreach (var t in baseList.Types) {
            if (t.Type.ToString() == "IAsyncDisposable")
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测是否是 void Dispose() 方法（同步释放）
    /// </summary>
    private static bool IsSyncDisposeMethod(MemberDeclarationSyntax member, out MethodDeclarationSyntax method) {
        method = null!;
        if (member is not MethodDeclarationSyntax m) return false;
        if (m.Identifier.ValueText != "Dispose") return false;
        if (m.ReturnType is not PredefinedTypeSyntax ret || !ret.Keyword.IsKind(SyntaxKind.VoidKeyword)) return false;
        if (m.ParameterList.Parameters.Count != 0) return false;
        method = m;
        return true;
    }

    /// <summary>
    /// 检测是否是 ValueTask DisposeAsync() 方法（异步释放）
    /// </summary>
    private static bool IsAsyncDisposeMethod(MemberDeclarationSyntax member, out MethodDeclarationSyntax method) {
        method = null!;
        if (member is not MethodDeclarationSyntax m) return false;
        if (m.Identifier.ValueText != "DisposeAsync") return false;
        if (m.ReturnType.ToString() != "ValueTask") return false;
        if (m.ParameterList.Parameters.Count != 0) return false;
        method = m;
        return true;
    }

    /// <summary>
    /// void Dispose() → ValueTask DisposeAsync()
    /// </summary>
    private static MethodDeclarationSyntax ConvertDisposeToDisposeAsync(MethodDeclarationSyntax method) {
        var newName = SyntaxFactory.Identifier("DisposeAsync").WithTriviaFrom(method.Identifier);

        // 改返回类型为 ValueTask
        var newReturnType = SyntaxFactory.IdentifierName("ValueTask").WithTriviaFrom(method.ReturnType);

        // 改方法体：在末尾加 return ValueTask.CompletedTask;
        var newBody = method.Body;
        if (newBody is not null) {
            var returnStmt = SyntaxFactory.ReturnStatement(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("ValueTask"),
                    SyntaxFactory.IdentifierName("CompletedTask")))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("            "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newStatements = newBody.Statements.Add(returnStmt);
            newBody = newBody.WithStatements(newStatements);
        }

        return method
            .WithIdentifier(newName)
            .WithReturnType(newReturnType)
            .WithBody(newBody);
    }

    /// <summary>
    /// ValueTask DisposeAsync() → void Dispose()
    /// </summary>
    private static MethodDeclarationSyntax ConvertDisposeAsyncToDispose(MethodDeclarationSyntax method) {
        var newName = SyntaxFactory.Identifier("Dispose").WithTriviaFrom(method.Identifier);

        // 改返回类型为 void
        var newReturnType = SyntaxFactory.PredefinedType(
            SyntaxFactory.Token(SyntaxKind.VoidKeyword)).WithTriviaFrom(method.ReturnType);

        // 改方法体：移除 return ValueTask.CompletedTask; 语句
        var newBody = method.Body;
        if (newBody is not null) {
            var newStatements = newBody.Statements.Where(s => !IsReturnCompletedTask(s)).ToList();
            newBody = newBody.WithStatements(SyntaxFactory.List(newStatements));
        }

        return method
            .WithIdentifier(newName)
            .WithReturnType(newReturnType)
            .WithBody(newBody);
    }

    private static bool IsReturnCompletedTask(StatementSyntax stmt) {
        if (stmt is not ReturnStatementSyntax ret) return false;
        if (ret.Expression is null) return false;
        return ret.Expression.ToString().Contains("ValueTask.CompletedTask");
    }

    // --- 调用点改写 ---

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
        var visited = (InvocationExpressionSyntax?)base.VisitInvocationExpression(node) ?? node;

        // Dispose() → await DisposeAsync().ConfigureAwait(false)
        if (_direction == FixDirection.Async && IsDisposeCall(visited)) {
            var newExpr = ConvertDisposeCallToDisposeAsyncCall(visited);
            if (newExpr is not null) {
                CallSiteChangeCount++;
                FixedCount++;
                return newExpr;
            }
        }

        // await DisposeAsync() → Dispose()
        if (_direction == FixDirection.Sync && IsAwaitDisposeAsyncCall(visited, out var disposeAsyncCall)) {
            var newExpr = ConvertDisposeAsyncCallToDisposeCall(disposeAsyncCall);
            CallSiteChangeCount++;
            FixedCount++;
            return newExpr;
        }

        return visited;
    }

    /// <summary>检测 expr.Dispose() 调用</summary>
    private static bool IsDisposeCall(InvocationExpressionSyntax node) {
        if (node.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (ma.Name.Identifier.ValueText != "Dispose") return false;
        if (node.ArgumentList.Arguments.Count != 0) return false;
        return true;
    }

    /// <summary>expr.Dispose() → await expr.DisposeAsync().ConfigureAwait(false)</summary>
    private ExpressionSyntax? ConvertDisposeCallToDisposeAsyncCall(InvocationExpressionSyntax node) {
        if (node.Expression is not MemberAccessExpressionSyntax ma) return null;

        var disposeAsyncAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ma.Expression.WithoutTrailingTrivia(),
            SyntaxFactory.IdentifierName("DisposeAsync"));

        var disposeAsyncCall = SyntaxFactory.InvocationExpression(disposeAsyncAccess,
            SyntaxFactory.ArgumentList());

        // 加 .ConfigureAwait(false)（测试代码不加）
        ExpressionSyntax awaited;
        if (_isTestFile) {
            awaited = disposeAsyncCall;
        } else {
            var configureAwait = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    disposeAsyncCall,
                    SyntaxFactory.IdentifierName("ConfigureAwait")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
            awaited = configureAwait;
        }

        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        var awaitExpr = SyntaxFactory.AwaitExpression(awaitKeyword, awaited);

        return awaitExpr
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
    }

    /// <summary>检测 await expr.DisposeAsync() 模式，返回内层 DisposeAsync 调用</summary>
    private static bool IsAwaitDisposeAsyncCall(InvocationExpressionSyntax node, out InvocationExpressionSyntax disposeAsyncCall) {
        disposeAsyncCall = node!;

        // node 可能是 expr.DisposeAsync() 或 expr.DisposeAsync().ConfigureAwait(false)
        // 先检查 ConfigureAwait 包装
        InvocationExpressionSyntax? innerCall = null;
        if (node.Expression is MemberAccessExpressionSyntax ma1 && ma1.Name.Identifier.ValueText == "ConfigureAwait") {
            if (ma1.Expression is InvocationExpressionSyntax inner) {
                innerCall = inner;
            }
        } else {
            innerCall = node;
        }

        if (innerCall is null) {
            disposeAsyncCall = node;
            return false;
        }

        if (innerCall.Expression is not MemberAccessExpressionSyntax ma2) {
            disposeAsyncCall = node;
            return false;
        }
        if (ma2.Name.Identifier.ValueText != "DisposeAsync") {
            disposeAsyncCall = node;
            return false;
        }
        if (innerCall.ArgumentList.Arguments.Count != 0) {
            disposeAsyncCall = node;
            return false;
        }

        disposeAsyncCall = innerCall;
        return true;
    }

    /// <summary>await expr.DisposeAsync() → expr.Dispose()</summary>
    private static ExpressionSyntax ConvertDisposeAsyncCallToDisposeCall(InvocationExpressionSyntax disposeAsyncCall) {
        if (disposeAsyncCall.Expression is not MemberAccessExpressionSyntax ma) return disposeAsyncCall;

        var disposeAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ma.Expression.WithoutTrailingTrivia(),
            SyntaxFactory.IdentifierName("Dispose"));

        var disposeCall = SyntaxFactory.InvocationExpression(disposeAccess,
            SyntaxFactory.ArgumentList());

        return disposeCall
            .WithLeadingTrivia(disposeAsyncCall.GetLeadingTrivia())
            .WithTrailingTrivia(disposeAsyncCall.GetTrailingTrivia());
    }

    // --- using 声明改写 ---

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) {
        var visited = (LocalDeclarationStatementSyntax?)base.VisitLocalDeclarationStatement(node) ?? node;

        if (_direction == FixDirection.Async) {
            // using var x → await using var x
            if (visited.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
                && visited.AwaitKeyword.IsKind(SyntaxKind.None)) {
                var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space);
                var newNode = visited.WithAwaitKeyword(awaitKeyword);
                UsingDeclChangeCount++;
                FixedCount++;
                return newNode;
            }
        } else {
            // await using var x → using var x
            if (visited.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) {
                var newNode = visited.WithAwaitKeyword(SyntaxFactory.Token(SyntaxKind.None));
                UsingDeclChangeCount++;
                FixedCount++;
                return newNode;
            }
        }

        return visited;
    }
}
