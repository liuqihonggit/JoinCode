namespace AotSafety.Generator {
    /// <summary>
    /// AOT 安全分析器共享辅助方法
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
        /// 判断是否在测试方法内
        /// </summary>
        public static bool IsInsideTestMethod(SyntaxNode node) {
            var current = node.Parent;
            var foundTestClass = false;
            while (current is not null) {
                if (current is MethodDeclarationSyntax methodDecl) {
                    if (methodDecl.AttributeLists.Any(al =>
                        al.Attributes.Any(a => {
                            var name = a.Name.ToString();
                            return name == "Fact" || name == "Theory" || name == "TestMethod" ||
                                   name == "Test" || name == "InlineData" ||
                                   name.Contains("Fact", StringComparison.Ordinal) ||
                                   name.Contains("Test", StringComparison.Ordinal);
                        })))
                        return true;
                }

                if (current is ClassDeclarationSyntax classDecl) {
                    var className = classDecl.Identifier.ValueText;
                    if (className.EndsWith("Tests", StringComparison.Ordinal) ||
                        className.EndsWith("Test", StringComparison.Ordinal))
                        foundTestClass = true;
                }

                if (current is BaseNamespaceDeclarationSyntax nsDecl) {
                    var nsName = nsDecl.Name.ToString();
                    if (nsName.EndsWith(".Tests", StringComparison.Ordinal) ||
                        nsName.EndsWith(".Test", StringComparison.Ordinal))
                        foundTestClass = true;
                }

                current = current.Parent;
            }
            return foundTestClass;
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
        /// <param name="type">要检查的类型符号</param>
        /// <param name="check">对每个方法执行的检查，返回 true 表示条件满足</param>
        /// <returns>如果调用链中任意方法满足条件，返回 true</returns>
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

        /// <summary>
        /// BFS 核心逻辑：从 Dispose 方法出发，跟踪同类型方法调用链
        /// </summary>
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

        /// <summary>
        /// 获取方法体中调用的同类型方法名（支持直接调用 Method() 和 this.Method()）
        /// </summary>
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

        /// <summary>
        /// 判断调用是否是 Interlocked.Exchange(ref field, null) 或 Volatile.Write(ref field, null)
        /// </summary>
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
    }
}