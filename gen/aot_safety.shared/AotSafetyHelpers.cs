namespace AotSafety.Shared;

/// <summary>
/// AOT 安全分析器共享辅助方法 — 供所有规则类调用。
/// </summary>
public static class AotSafetyHelpers {
    /// <summary>
    /// 检查节点是否在循环内（for/while/do/foreach）
    /// </summary>
    public static bool IsInsideLoop(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// 查找包含指定节点的类型声明
    /// </summary>
    public static TypeDeclarationSyntax? FindEnclosingTypeDeclaration(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is TypeDeclarationSyntax typeDecl)
                return typeDecl;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// 找到包含指定节点的最近方法声明
    /// </summary>
    public static MethodDeclarationSyntax? FindEnclosingMethodDeclaration(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax method)
                return method;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// 判断方法名是否是释放方法（Dispose/DisposeAsync/DisposeCore/Release/Close/Shutdown/ShutdownAsync/StopAsync）
    /// </summary>
    public static bool IsDisposeMethodName(string methodName) {
        var nameSpan = methodName.AsSpan();
        if (nameSpan.StartsWith("Dispose".AsSpan(), StringComparison.Ordinal)) return true;
        if (nameSpan.SequenceEqual("Release".AsSpan())) return true;
        if (nameSpan.SequenceEqual("Close".AsSpan())) return true;
        if (nameSpan.SequenceEqual("Shutdown".AsSpan())) return true;
        if (nameSpan.SequenceEqual("ShutdownAsync".AsSpan())) return true;
        if (nameSpan.SequenceEqual("StopAsync".AsSpan())) return true;
        return false;
    }

    /// <summary>
    /// 从 Dispose 方法出发，BFS 跟踪方法调用链，检查是否有任意方法满足条件。
    /// 支持 partial class（合并所有 partial 声明的方法）、this.Method() 调用、方法过载。
    /// </summary>
    public static bool CheckInDisposeCallChain(
        INamedTypeSymbol type,
        Func<MethodDeclarationSyntax, bool> check) {
        var allMethods = new List<MethodDeclarationSyntax>();
        foreach (var refDecl in type.DeclaringSyntaxReferences) {
            if (refDecl.GetSyntax() is TypeDeclarationSyntax typeDecl)
                allMethods.AddRange(typeDecl.Members.OfType<MethodDeclarationSyntax>());
        }

        return CheckInDisposeCallChainCore(allMethods, check);
    }

    private static bool CheckInDisposeCallChainCore(
        List<MethodDeclarationSyntax> allMethods,
        Func<MethodDeclarationSyntax, bool> check) {
        var methodsByName = allMethods
            .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var disposeMethods = allMethods
            .Where(m => IsDisposeMethodName(m.Identifier.ValueText));

        var visited = new HashSet<MethodDeclarationSyntax>();
        var queue = new Queue<MethodDeclarationSyntax>();

        foreach (var m in disposeMethods)
            queue.Enqueue(m);

        while (queue.Count > 0) {
            var method = queue.Dequeue();
            if (!visited.Add(method)) continue;

            if (check(method)) return true;

            foreach (var calledName in GetCalledMethodNamesInSameType(method)) {
                if (methodsByName.TryGetValue(calledName, out var calledMethods))
                    foreach (var cm in calledMethods)
                        queue.Enqueue(cm);
            }
        }

        return false;
    }

    private static IEnumerable<string> GetCalledMethodNamesInSameType(MethodDeclarationSyntax method) {
        SyntaxNode? body = method.Body;
        if (body is null && method.ExpressionBody is not null)
            body = method.ExpressionBody.Expression;
        if (body is null) yield break;

        foreach (var inv in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()) {
            switch (inv.Expression) {
                case IdentifierNameSyntax id:
                yield return id.Identifier.ValueText;
                break;
                case MemberAccessExpressionSyntax ma when ma.Expression is ThisExpressionSyntax:
                if (ma.Name is IdentifierNameSyntax nameId)
                    yield return nameId.Identifier.ValueText;
                break;
            }
        }
    }

    /// <summary>
    /// 获取方法体 SyntaxNode（支持普通 body 和 expression body）
    /// </summary>
    public static SyntaxNode? GetMethodBody(MethodDeclarationSyntax method) {
        if (method.Body is not null) return method.Body;
        if (method.ExpressionBody is not null) return method.ExpressionBody.Expression;
        return null;
    }

    /// <summary>
    /// 检查方法体是否引用了指定字段名（AST 语义分析）
    /// </summary>
    public static bool MethodBodyReferencesField(MethodDeclarationSyntax method, string fieldName) {
        var body = GetMethodBody(method);
        if (body is null) return false;

        return body.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.ValueText == fieldName);
    }

    /// <summary>
    /// 检查方法体是否将指定字段置 null（AST 语义分析）
    /// 检测模式: field = null, field = null!, Interlocked.Exchange(ref field, null), Volatile.Write(ref field, null)
    /// </summary>
    public static bool MethodBodyNullsField(MethodDeclarationSyntax method, string fieldName) {
        if (method.Body is null) return false;

        if (method.Body.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.SimpleAssignmentExpression))
            .Any(a =>
                a.Left is IdentifierNameSyntax leftId &&
                leftId.Identifier.ValueText == fieldName &&
                IsNullLiteral(a.Right)))
            return true;

        return method.Body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => IsAtomicNullWriteToField(inv, fieldName));
    }

    private static bool IsAtomicNullWriteToField(InvocationExpressionSyntax invocation, string fieldName) {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (ma.Expression is not IdentifierNameSyntax typeId) return false;

        var typeName = typeId.Identifier.ValueText.AsSpan();
        var methodName = ma.Name.Identifier.ValueText.AsSpan();

        var isInterlockedExchange = typeName.SequenceEqual("Interlocked".AsSpan()) &&
                                    methodName.SequenceEqual("Exchange".AsSpan());
        var isVolatileWrite = typeName.SequenceEqual("Volatile".AsSpan()) &&
                              methodName.SequenceEqual("Write".AsSpan());

        if (!isInterlockedExchange && !isVolatileWrite) return false;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2) return false;

        if (!args[0].RefKindKeyword.IsKind(SyntaxKind.RefKeyword)) return false;
        if (args[0].Expression is not IdentifierNameSyntax refId) return false;
        if (refId.Identifier.ValueText != fieldName) return false;

        return IsNullLiteral(args[1].Expression);
    }

    /// <summary>
    /// 判断表达式是否是 null 字面量（含 null! 抑制警告形式）
    /// </summary>
    public static bool IsNullLiteral(ExpressionSyntax expr) {
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression))
            return true;

        if (expr is PostfixUnaryExpressionSyntax postfix &&
            postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
            postfix.Operand is LiteralExpressionSyntax operandLiteral &&
            operandLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            return true;

        return false;
    }

    public static bool HasConfigureAwaitFalse(AwaitExpressionSyntax awaitExpr) {
        if (awaitExpr.Expression is InvocationExpressionSyntax configureAwaitInvocation) {
            if (configureAwaitInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "ConfigureAwait") {
                var args = configureAwaitInvocation.ArgumentList.Arguments;
                if (args.Count == 1) {
                    var arg = args[0].Expression;
                    if (arg is LiteralExpressionSyntax literal &&
                        literal.Token.IsKind(SyntaxKind.FalseKeyword)) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public static bool HasConfigureAwaitAny(AwaitExpressionSyntax awaitExpr) {
        if (awaitExpr.Expression is InvocationExpressionSyntax configureAwaitInvocation) {
            if (configureAwaitInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "ConfigureAwait") {
                return true;
            }
        }
        return false;
    }

    public static bool IsTaskYield(AwaitExpressionSyntax awaitExpr) {
        if (awaitExpr.Expression is InvocationExpressionSyntax invocation) {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Yield") {
                return true;
            }
        }
        return false;
    }

    // ============================================================
    // 交互输入辅助方法 (JCC2001/2002/2003 共享)
    // ============================================================

    public static bool IsInsideIsInputRedirectedCheck(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is IfStatementSyntax ifStatement) {
                if (ContainsIsInputRedirectedCheck(ifStatement.Condition)) {
                    if (IsInProtectedBranch(node, ifStatement))
                        return true;
                }
            } else if (current is ConditionalExpressionSyntax conditional) {
                if (ContainsIsInputRedirectedCheck(conditional.Condition))
                    return true;
            }

            if (current is BlockSyntax block) {
                if (IsProtectedByEarlyReturnInBlock(node, block))
                    return true;
            }

            current = current.Parent;
        }
        return false;
    }

    private static bool IsInProtectedBranch(SyntaxNode node, IfStatementSyntax ifStatement) {
        var condition = ifStatement.Condition;
        var isTopLevelNegated = IsNegatedCondition(condition);
        var containsInnerNegation = ContainsNegatedIsInputRedirected(condition);

        if (isTopLevelNegated) {
            if (ifStatement.Statement is not null && IsDescendantOf(node, ifStatement.Statement))
                return true;
        } else if (containsInnerNegation) {
            if (ifStatement.Statement is not null && IsDescendantOf(node, ifStatement.Statement))
                return true;
        } else {
            if (ifStatement.Else is not null && IsDescendantOf(node, ifStatement.Else))
                return true;
        }

        return false;
    }

    private static bool ContainsNegatedIsInputRedirected(ExpressionSyntax condition) {
        foreach (var descendant in condition.DescendantNodesAndSelf()) {
            if (descendant is PrefixUnaryExpressionSyntax prefix
                && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken)) {
                var innerText = prefix.Operand.ToString().Replace(" ", "");
                if (innerText.Contains("Console.IsInputRedirected") || innerText.Contains("System.Console.IsInputRedirected"))
                    return true;
                if (innerText.Contains("TestEnvironmentDetector.IsNonInteractive"))
                    return true;
                if (innerText.Contains("TestEnvironmentDetector.IsTestEnvironment"))
                    return true;
            }
        }
        return false;
    }

    private static bool IsNegatedCondition(ExpressionSyntax condition) {
        if (condition is PrefixUnaryExpressionSyntax prefix && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
            return true;
        return false;
    }

    private static bool IsProtectedByEarlyReturnInBlock(SyntaxNode node, BlockSyntax block) {
        var statement = FindAncestorStatement(node);
        if (statement is null) return false;

        var nodeIndex = block.Statements.IndexOf(statement);
        if (nodeIndex < 0) return false;

        for (var i = 0; i < nodeIndex; i++) {
            var stmt = block.Statements[i];
            if (stmt is IfStatementSyntax ifStmt && ContainsIsInputRedirectedCheck(ifStmt.Condition)) {
                if (IfStatementExitsEarly(ifStmt))
                    return true;
            }
        }

        return false;
    }

    private static StatementSyntax? FindAncestorStatement(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is StatementSyntax statement)
                return statement;
            current = current.Parent;
        }
        return null;
    }

    private static bool IfStatementExitsEarly(IfStatementSyntax ifStmt) {
        return BlockContainsExit(ifStmt.Statement);
    }

    private static bool BlockContainsExit(StatementSyntax statement) {
        switch (statement) {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
            case BreakStatementSyntax:
            case ContinueStatementSyntax:
            return true;
            case BlockSyntax block:
            foreach (var stmt in block.Statements) {
                if (BlockContainsExit(stmt))
                    return true;
            }
            return false;
            default:
            return false;
        }
    }

    private static bool IsDescendantOf(SyntaxNode node, SyntaxNode ancestor) {
        var current = node;
        while (current is not null) {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    public static bool ContainsIsInputRedirectedCheck(ExpressionSyntax condition) {
        foreach (var descendant in condition.DescendantNodesAndSelf()) {
            if (descendant is MemberAccessExpressionSyntax memberAccess) {
                var text = memberAccess.ToString().Replace(" ", "");
                if (text.Contains("Console.IsInputRedirected") || text.Contains("System.Console.IsInputRedirected"))
                    return true;
                if (text.Contains("TestEnvironmentDetector.IsNonInteractive"))
                    return true;
                if (text.Contains("TestEnvironmentDetector.IsTestEnvironment"))
                    return true;
            }
        }
        return false;
    }

    public static bool IsInsideIfDebugDirective(SyntaxNode node) {
        var current = node;
        while (current is not null) {
            foreach (var trivia in current.GetLeadingTrivia()) {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia)) {
                    var text = trivia.ToString();
                    if (text.Contains("DEBUG"))
                        return true;
                }
            }
            current = current.Parent;
        }

        if (node.Parent is not null) {
            foreach (var trivia in node.Parent.GetLeadingTrivia()) {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia)) {
                    var text = trivia.ToString();
                    if (text.Contains("DEBUG"))
                        return true;
                }
            }
        }

        return false;
    }

    // ============================================================
    // async void 辅助方法 (JCC3005)
    // ============================================================

    public static bool IsUiEventHandler(string methodName) {
        var eventSuffixes = new[] {
            "_Click", "_Changed", "_Loaded", "_Closing", "_Closed",
            "_Activated", "_Deactivated", "_GotFocus", "_LostFocus",
            "_KeyDown", "_KeyUp", "_KeyPress", "_MouseEnter", "_MouseLeave",
            "_SelectedIndexChanged", "_TextChanged", "_CheckedChanged",
            "OnClick", "OnChanged", "OnLoaded", "OnClosing", "OnClosed",
            "OnCreated", "OnDeleted", "OnRenamed", "OnFileChanged", "OnFileRenamed",
        };

        foreach (var suffix in eventSuffixes) {
            if (methodName.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool IsTimerCallbackPattern(string methodName) {
        var callbackPrefixes = new[] { "Process", "Handle", "OnTimer", "TimerCallback" };
        foreach (var prefix in callbackPrefixes) {
            if (methodName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // ============================================================
    // 阻塞调用辅助方法 (JCC3006)
    // ============================================================

    public static bool IsInsideMainMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax methodDecl &&
                methodDecl.Identifier.ValueText == "Main")
                return true;
            current = current.Parent;
        }
        return false;
    }

    public static bool IsInsideConstructor(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is ConstructorDeclarationSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    public static bool IsInsideSyncMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax methodDecl) {
                if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
                    return true;
                return false;
            }
            if (current is LambdaExpressionSyntax lambda) {
                if (!lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                    return true;
                return false;
            }
            if (current is ConstructorDeclarationSyntax)
                return false;
            if (current is MethodDeclarationSyntax { Identifier.ValueText: "Dispose" or "DisposeAsync" })
                return false;
            current = current.Parent;
        }
        return false;
    }

    public static bool IsInsideDisposeMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax methodDecl) {
                var name = methodDecl.Identifier.ValueText;
                if (name is "Dispose" or "DisposeAsync")
                    return true;
                return false;
            }
            current = current.Parent;
        }
        return false;
    }

    // ============================================================
    // Process deadlock 辅助方法 (JCC3003)
    // ============================================================

    public static InvocationExpressionSyntax? FindMethodInvocation(ExpressionSyntax expr, string methodName) {
        if (expr is InvocationExpressionSyntax inv) {
            if (inv.Expression is MemberAccessExpressionSyntax directAccess &&
                directAccess.Name.Identifier.ValueText == methodName) {
                return inv;
            }

            if (inv.Expression is MemberAccessExpressionSyntax chainAccess &&
                chainAccess.Expression is InvocationExpressionSyntax innerInv) {
                var result = FindMethodInvocation(innerInv, methodName);
                if (result is not null) return result;
            }
        }

        return null;
    }

    public static string? GetProcessVariableName(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess) {
            return memberAccess.Expression?.ToString();
        }
        return null;
    }

    public static InvocationExpressionSyntax? FindReadToEndAsyncCall(SyntaxNode node, string processVariableName) {
        foreach (var descendant in node.DescendantNodesAndSelf()) {
            if (descendant is InvocationExpressionSyntax invocation) {
                var symbolInfo = invocation.Expression;
                if (symbolInfo is MemberAccessExpressionSyntax memberAccess) {
                    if (memberAccess.Name.Identifier.ValueText == "ReadToEndAsync") {
                        var leftStr = memberAccess.Expression?.ToString() ?? "";
                        if ((leftStr.Contains("StandardOutput") || leftStr.Contains("StandardError")) &&
                            leftStr.StartsWith(processVariableName, StringComparison.Ordinal)) {
                            return invocation;
                        }
                    }
                }
            }
        }
        return null;
    }

    // ============================================================
    // Sequential await in loop 辅助方法 (JCC3007)
    // ============================================================

    public static SyntaxNode? FindInnermostLoop(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is ForEachStatementSyntax or ForEachVariableStatementSyntax or
                ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                return current;
            current = current.Parent;
        }
        return null;
    }

    public static bool ContainsCancellationTokenCondition(SyntaxNode? condition) {
        if (condition is null) return false;
        foreach (var desc in condition.DescendantNodesAndSelf()) {
            if (desc is IdentifierNameSyntax identifier) {
                var name = identifier.Identifier.ValueText;
                if (name.Contains("Cancellation", StringComparison.Ordinal) ||
                    name.Contains("cancellationToken", StringComparison.Ordinal) ||
                    name == "ct" || name == "token")
                    return true;
            }

            if (desc is MemberAccessExpressionSyntax memberAccess) {
                var name = memberAccess.Name.Identifier.ValueText;
                if (name.Contains("Cancellation", StringComparison.Ordinal) ||
                    name == "IsRunning" || name == "IsConnected")
                    return true;
            }
        }
        return false;
    }

    public static bool LoopHasEarlyExit(SyntaxNode loop) {
        SyntaxNode body = loop switch {
            ForEachStatementSyntax f => f.Statement,
            ForEachVariableStatementSyntax f => f.Statement,
            ForStatementSyntax f => f.Statement,
            WhileStatementSyntax w => w.Statement,
            DoStatementSyntax d => d.Statement,
            _ => throw new InvalidOperationException(),
        };

        foreach (var desc in body.DescendantNodes()) {
            if (desc is BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax)
                return true;
        }
        return false;
    }

    // ============================================================
    // Unread stderr 辅助方法 (JCC3004)
    // ============================================================

    public static SyntaxNode? FindEnclosingClassBlock(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax) {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    public static bool HasRedirectStandardErrorTrue(ObjectCreationExpressionSyntax objectCreation, SyntaxNodeAnalysisContext ctx) {
        if (objectCreation.Initializer is not null) {
            foreach (var initializer in objectCreation.Initializer.Expressions) {
                if (initializer is AssignmentExpressionSyntax assignment) {
                    var left = assignment.Left.ToString().Replace(" ", "");
                    if (left == "RedirectStandardError") {
                        var right = assignment.Right.ToString().Trim();
                        if (right == "true") return true;
                    }
                }
            }
        }

        var variableName = GetProcessStartInfoVariableName(objectCreation);
        if (variableName is not null) {
            var enclosingBlock = FindEnclosingClassBlock(objectCreation);
            if (enclosingBlock is not null) {
                foreach (var descendant in enclosingBlock.DescendantNodes()) {
                    if (descendant is AssignmentExpressionSyntax assignment) {
                        var left = assignment.Left.ToString().Replace(" ", "");
                        if (left == $"{variableName}.RedirectStandardError") {
                            var right = assignment.Right.ToString().Trim();
                            if (right == "true") return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    public static string? GetProcessStartInfoVariableName(ObjectCreationExpressionSyntax objectCreation) {
        if (objectCreation.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax variableDeclarator) {
            return variableDeclarator.Identifier.ValueText;
        }

        return null;
    }

    public static bool HasStandardErrorConsumption(SyntaxNode block) {
        foreach (var descendant in block.DescendantNodes()) {
            if (descendant is MemberAccessExpressionSyntax memberAccess) {
                var name = memberAccess.Name.Identifier.ValueText;
                if (name == "StandardError") return true;
            }

            if (descendant is InvocationExpressionSyntax invocation) {
                if (invocation.Expression is MemberAccessExpressionSyntax invMemberAccess) {
                    if (invMemberAccess.Name.Identifier.ValueText == "BeginErrorReadLine") return true;
                }
            }

            if (descendant is AssignmentExpressionSyntax eventAssignment) {
                var left = eventAssignment.Left.ToString().Replace(" ", "");
                if (left.Contains("ErrorDataReceived")) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断类型是否为 Task/ValueTask/Task{T}/ValueTask{T} — BCL 异步契约类型，通用判断不依赖项目特定类型。
    /// </summary>
    public static bool IsTaskLikeType(ITypeSymbol? type) {
        if (type is null) return false;
        var name = type.OriginalDefinition.ToDisplayString();
        return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.Task<T>" or "System.Threading.Tasks.ValueTask<T>";
    }

    /// <summary>
    /// 判断方法符号是否返回 Task-like 类型（BCL 异步契约）。
    /// </summary>
    public static bool ReturnsTaskLike(IMethodSymbol? method) {
        if (method is null) return false;
        return IsTaskLikeType(method.ReturnType);
    }

    /// <summary>
    /// 判断方法是否返回 Task-like 类型或含 Task 的集合（IReadOnlyList&lt;Task&gt;、Task[]、IEnumerable&lt;Task&gt; 等）。
    /// </summary>
    public static bool ReturnsTaskOrTaskCollection(IMethodSymbol? method) {
        if (method is null) return false;
        if (IsTaskLikeType(method.ReturnType)) return true;
        return IsTaskCollectionType(method.ReturnType);
    }

    /// <summary>
    /// 判断类型是否为含 Task 的集合（数组/泛型集合，元素类型为 Task-like）。
    /// </summary>
    public static bool IsTaskCollectionType(ITypeSymbol? type) {
        if (type is null) return false;

        if (type is IArrayTypeSymbol array)
            return IsTaskLikeType(array.ElementType);

        if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            return IsTaskLikeType(named.TypeArguments[0]);

        return false;
    }

    /// <summary>
    /// 判断表达式语句是否在 await 上下文内（祖先含 AwaitExpressionSyntax）。
    /// </summary>
    public static bool IsInsideAwait(SyntaxNode node) {
        return node.Ancestors().Any(a => a is AwaitExpressionSyntax);
    }

    /// <summary>
    /// 判断节点是否在 lambda/本地函数内（非直接方法体）。
    /// </summary>
    public static bool IsInsideLambdaOrLocalFunction(SyntaxNode node, SyntaxNode methodBody) {
        var current = node.Parent;
        while (current is not null && current != methodBody) {
            if (current is SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                LocalFunctionStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// 判断方法是否实现 IDisposable.Dispose 或 IAsyncDisposable.DisposeAsync（语义检测，不靠方法名）。
    /// 检测显式接口实现和 override 链。
    /// </summary>
    public static bool IsDisposeInterfaceImplementation(IMethodSymbol method) {
        if (method is null) return false;
        for (var m = method; m is not null; m = m.OverriddenMethod) {
            if (m.ExplicitInterfaceImplementations.Any(i =>
                i.ContainingType?.Name is "IDisposable" or "IAsyncDisposable")) return true;
        }
        return false;
    }

    /// <summary>
    /// 判断方法是否为释放方法 — 语义检测 IDisposable/IAsyncDisposable 实现，或方法名为 Dispose/DisposeAsync（BCL 契约）。
    /// </summary>
    public static bool IsDisposeMethod(IMethodSymbol method) {
        if (method is null) return false;
        var name = method.Name;
        if (name is "Dispose" or "DisposeAsync") return true;
        return IsDisposeInterfaceImplementation(method);
    }
}
