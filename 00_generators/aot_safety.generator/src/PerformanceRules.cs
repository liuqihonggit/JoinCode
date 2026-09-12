namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PerformanceRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleStringConcatInLoop = new(
            "JCC5002",
            "性能: 循环内字符串拼接使用 += 运算符",
            "在循环内使用 += 拼接字符串会导致大量中间字符串分配。应使用 StringBuilder 或 string.Concat 替代。",
            "PerformanceSafety",
            DiagnosticSeverity.Warning,
            true,
            "Strings are immutable; each += creates a new string object. Accumulating concatenation in a loop causes O(n^2) memory allocation. StringBuilder is mutable with O(1) append and O(n) final ToString.");

        private static readonly DiagnosticDescriptor RuleNestedLoopOnSameCollection = new(
            "JCC6001",
            "性能: 嵌套循环遍历同一集合，潜在 O(n²) 复杂度",
            "嵌套循环在第 {0} 行遍历集合，外层循环在第 {1} 行也遍历集合。如果内层循环体依赖外层集合，复杂度为 O(n²)。考虑使用 HashSet/Dictionary 替代内层线性查找，或使用双指针/排序+二分查找优化。",
            "PerformanceAudit",
            DiagnosticSeverity.Warning,
            true,
            "嵌套循环遍历同一集合是最常见的 O(n²) 来源. 优化方案优先级: 1) 编译期确定性优化(static readonly + 二分查找) 2) 空间换时间(HashSet/Dictionary 替代 List.Contains) 3) 对数复杂度算法(排序 + BinarySearch/LowerBound+UpperBound).");

        private static readonly DiagnosticDescriptor RuleLinearOperationInLoop = new(
            "JCC6002",
            "性能: 循环内调用 O(n) 操作 '{0}'，整体复杂度为 O(n²)",
            "在循环内调用 '{0}'（O(n) 操作），整体复杂度为 O(n²)。建议: Contains/IndexOf/Find → 用 HashSet<T> 替代(查找 O(1)); RemoveAt → 从尾部删除或用 Queue/Stack; Insert(0,..) → 用 LinkedList 或从尾部添加后 Reverse。",
            "PerformanceAudit",
            DiagnosticSeverity.Warning,
            true,
            "循环内线性操作是最常见的 O(n²) 模式. List<T>.Contains/IndexOf/Find 是 O(n)，在循环内调用导致 O(n²). HashSet<T>.Contains 是 O(1)，Dictionary<TKey,TValue> 查找也是 O(1).");

        private static readonly DiagnosticDescriptor RuleListContainsToHashSet = new(
            "JCC6003",
            "性能: List<T> 在方法内调用 Contains 共 {0} 次，建议替换为 HashSet<T>",
            "变量 '{1}' (List<{2}>) 在此方法内调用 Contains 共 {0} 次。List.Contains 是 O(n)，HashSet.Contains 是 O(1)。如果元素不需要重复且顺序不重要，建议将类型改为 HashSet<{2}>。",
            "PerformanceAudit",
            DiagnosticSeverity.Warning,
            true,
            "频繁调用 List.Contains 是典型的空间换时间优化场景. HashSet<T> 的 Contains 是 O(1)，但会失去索引访问和元素顺序. 如果需要保留顺序，可用 HashSet 查找 + List 遍历的双数据结构模式.");

        private static readonly DiagnosticDescriptor RuleStaticReadOnlyLinearSearch = new(
            "JCC6004",
            "性能: static readonly 集合 '{0}' 上的线性查找，建议使用二分查找",
            "字段 '{0}' 是 static readonly，数据在编译期确定且运行时不变。对其调用 {1} 是 O(n) 线性查找。如果集合已排序，可使用 Array.BinarySearch (O(log n)) 替代；如果需要范围查询，使用 LowerBound/UpperBound 双二分查找。",
            "PerformanceAudit",
            DiagnosticSeverity.Info,
            true,
            "编译期确定性优化是最优方案. static readonly 数据不会在运行时改变，排序一次后可用二分查找. Array.BinarySearch 是 O(log n)，比 Contains/IndexOf 的 O(n) 快数十倍. 范围查询用 LowerBound+UpperBound 也是 O(log n).");

        private static readonly DiagnosticDescriptor RuleListInsertAtHead = new(
            "JCC6005",
            "性能: List<T>.Insert(0, item) 头部插入是 O(n) 操作",
            "List<T>.Insert(0, item) 需要移动所有现有元素，复杂度 O(n)。如果频繁头部插入，建议: 1) 从尾部 Add 后 Reverse; 2) 使用 Stack<T> (LIFO); 3) 使用 LinkedList<T> (但随机访问 O(n))。",
            "PerformanceAudit",
            DiagnosticSeverity.Warning,
            true,
            "List<T> 底层是数组，Insert(0, item) 需要 Array.Copy 移动所有元素. 在循环内 Insert(0,..) 是 O(n²). 替代方案: Add + Reverse 是 O(n); Stack<T>.Push 是 O(1); LinkedList<T>.AddFirst 是 O(1) 但失去索引访问.");

        private static readonly DiagnosticDescriptor RuleRangeQueryBinarySearch = new(
            "JCC6006",
            "性能: 循环中的范围查询可使用二分查找优化为 O(log n)",
            "循环中对已排序集合执行范围条件判断（'{0}'）是 O(n) 扫描。如果集合已排序，使用 Array.BinarySearch 或自定义 LowerBound/UpperBound 可将范围查询优化为 O(log n)。",
            "PerformanceAudit",
            DiagnosticSeverity.Info,
            true,
            "已排序集合的范围查询（如 arr[i] >= low && arr[i] <= high）不需要遍历全部元素. 二分查找定位下界和上界即可确定范围. Array.BinarySearch 是 O(log n). 自定义 LowerBound/UpperBound 也是 O(log n).");

        private static readonly DiagnosticDescriptor RuleStringToSpan = new(
            "JCC6007",
            "性能: 字符串操作 '{0}' 可优化为 Span<char> 减少分配",
            "在热路径中对 string 调用 {0} 会产生中间字符串分配。使用 ReadOnlySpan<char> 或 AsSpan() 可避免分配，提升性能。",
            "PerformanceAudit",
            DiagnosticSeverity.Info,
            true,
            "string.Substring/Remove/Split 等操作会创建新字符串对象. Span<char> 是栈上结构体，不分配堆内存. AsSpan() 是零拷贝切片. 在高频调用路径中差异显著.");

        private static readonly DiagnosticDescriptor RuleForeachToLinq = new(
            "JCC6008",
            "代码风格: foreach 循环可替换为 LINQ 链式表达式",
            "foreach 循环体仅包含 {0} 操作，可替换为更简洁的 LINQ 链式表达式: {1}。声明式代码更易读、更易维护。",
            "PerformanceAudit",
            DiagnosticSeverity.Info,
            true,
            "LINQ 链式编程是 C# 的核心范式. foreach + if + Add → .Where().ToList(); foreach + break → .FirstOrDefault(); foreach + return true → .Any(); foreach + sum += → .Sum(). 声明式代码意图更清晰，减少样板代码和 off-by-one 错误.");

        private static readonly DiagnosticDescriptor RuleParallelForEach = new(
            "JCC6009",
            "代码规范: 禁止 Parallel.For/ForEach/ForEachAsync，使用 AsParallel() 或 Task.WhenAll 替代",
            "Parallel.For/ForEach/ForEachAsync 不符合 LINQ 链式编程风格。使用 AsParallel() PLINQ 链式或 Task.WhenAll 替代。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "Parallel 的问题: 1) 命令式风格，不符合 LINQ 链式编程; 2) 异常以 AggregateException 抛出, 难以定位根因; 3) 共享状态需要加锁, 易死锁; 4) 不支持 async/await. 替代方案: 1) PLINQ 链式: items.AsParallel().WithDegreeOfParallelism(n).Select(x => Process(x)).ToList(); 2) 异步并发: await Task.WhenAll(items.Select(x => ProcessAsync(x))); 3) 限流并发: await Parallel.ForEachAsync(items, options, async (item, ct) => ...).");

        private static readonly DiagnosticDescriptor RuleLongLinqChain = new(
            "JCC6010",
            "代码风格: LINQ链式语法超过8句应拆分为有名函数",
            "LINQ链式表达式中包含 {0} 个方法调用，超过8句上限。应将部分操作提取为有意义命名的函数，主流程保持 <= 8 句链式调用。",
            "CodeStyle",
            DiagnosticSeverity.Info,
            true,
            "Long LINQ chains reduce readability. Extract part of the chain into a well-named function, keeping the main flow <= 8 chained calls.");

        private static readonly HashSet<string> LinearOperationMethods = new(StringComparer.Ordinal)
        {
            "Contains", "IndexOf", "Find", "FindIndex", "FindLast", "FindLastIndex",
            "RemoveAt", "Reverse",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                RuleStringConcatInLoop,
                RuleNestedLoopOnSameCollection, RuleLinearOperationInLoop,
                RuleListContainsToHashSet, RuleListInsertAtHead,
                RuleStaticReadOnlyLinearSearch, RuleStringToSpan,
                RuleRangeQueryBinarySearch, RuleForeachToLinq, RuleParallelForEach,
                RuleLongLinqChain);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeStringConcatInLoop, SyntaxKind.AddAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeNestedLoop, SyntaxKind.ForEachStatement, SyntaxKind.ForStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement);
            context.RegisterSyntaxNodeAction(AnalyzeLinearOperationInLoop, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeListInsertAtHead, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeMethodLevelListContains, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeStaticReadOnlyLinearSearch, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeStringToSpan, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeRangeQueryBinarySearch, SyntaxKind.ForEachStatement, SyntaxKind.ForStatement);
            context.RegisterSyntaxNodeAction(AnalyzeForeachToLinq, SyntaxKind.ForEachStatement);
            context.RegisterSyntaxNodeAction(AnalyzeParallelForEach, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeLongLinqChain, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeStringConcatInLoop(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var addAssignment = (AssignmentExpressionSyntax)ctx.Node;

            var leftType = ctx.SemanticModel.GetTypeInfo(addAssignment.Left).Type;
            if (leftType is null) return;
            if (leftType.SpecialType != SpecialType.System_String) return;

            if (!AotSafetyHelpers.IsInsideLoop(addAssignment)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleStringConcatInLoop, addAssignment.GetLocation()));
        }

        private static void AnalyzeNestedLoop(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var loopNode = ctx.Node;

            var depth = 0;
            var outerLoopLine = -1;
            var current = loopNode.Parent;
            while (current is not null)
            {
                if (current is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                    or ForEachStatementSyntax or ForEachVariableStatementSyntax)
                {
                    depth++;
                    if (outerLoopLine < 0)
                        outerLoopLine = current.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                }
                current = current.Parent;
            }

            if (depth < 1) return;

            var innerLoopLine = loopNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            var innerCollection = GetLoopCollectionExpression(loopNode);
            if (innerCollection is null) return;

            var parent = loopNode.Parent;
            while (parent is not null)
            {
                if (parent is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                    or ForEachStatementSyntax or ForEachVariableStatementSyntax)
                {
                    var outerCollection = GetLoopCollectionExpression(parent);
                    if (outerCollection is not null)
                    {
                        var innerText = innerCollection.ToString().Replace(" ", "");
                        var outerText = outerCollection.ToString().Replace(" ", "");

                        if (innerText == outerText ||
                            innerText.StartsWith(outerText + ".", StringComparison.Ordinal) ||
                            innerText.Contains(outerText))
                        {
                            ctx.ReportDiagnostic(Diagnostic.Create(
                                RuleNestedLoopOnSameCollection,
                                loopNode.GetLocation(),
                                innerLoopLine, outerLoopLine));
                            return;
                        }
                    }
                }
                parent = parent.Parent;
            }
        }

        private static ExpressionSyntax? GetLoopCollectionExpression(SyntaxNode loopNode)
        {
            return loopNode switch
            {
                ForEachStatementSyntax foreachStmt => foreachStmt.Expression,
                ForEachVariableStatementSyntax foreachVarStmt => foreachVarStmt.Expression,
                ForStatementSyntax forStmt => null,
                _ => null
            };
        }

        private static void AnalyzeLinearOperationInLoop(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (!AotSafetyHelpers.IsInsideLoop(invocation)) return;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (!LinearOperationMethods.Contains(methodName)) return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (!IsListOrArrayType(containingType)) return;

            if (methodName == "RemoveAt" && IsTailRemoval(invocation, ctx)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleLinearOperationInLoop,
                invocation.GetLocation(),
                $"{typeName}.{methodName}"));
        }

        private static bool IsTailRemoval(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext ctx)
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0) return false;

            var firstArg = args[0].Expression;

            var constantValue = ctx.SemanticModel.GetConstantValue(firstArg);
            if (constantValue.HasValue) return false;

            if (IsCountMinusOne(firstArg)) return true;

            if (firstArg is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.IndexExpression))
            {
                var operandText = prefixUnary.Operand.ToString().Trim();
                if (operandText == "1") return true;
            }

            if (firstArg is IdentifierNameSyntax identifier)
            {
                var varName = identifier.Identifier.ValueText;
                if (IsVariableAssignedAsCountMinusOne(identifier, varName, ctx)) return true;
            }

            return false;
        }

        private static bool IsCountMinusOne(ExpressionSyntax expr)
        {
            if (expr is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.SubtractExpression) &&
                binary.Right is LiteralExpressionSyntax literal &&
                literal.Token.ValueText == "1")
            {
                var leftText = binary.Left.ToString().Replace(" ", "");
                if (leftText.EndsWith(".Count", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsVariableAssignedAsCountMinusOne(IdentifierNameSyntax identifier, string varName, SyntaxNodeAnalysisContext ctx)
        {
            var statement = identifier.Parent;
            while (statement is not null && statement is not StatementSyntax)
                statement = statement.Parent;
            if (statement is null) return false;

            var block = statement.Parent;
            if (block is not BlockSyntax and not SwitchSectionSyntax) return false;

            var removeAtSpanStart = statement.SpanStart;

            foreach (var child in block.ChildNodes())
            {
                if (child.SpanStart >= removeAtSpanStart) break;

                if (child is LocalDeclarationStatementSyntax localDecl)
                {
                    foreach (var v in localDecl.Declaration.Variables)
                    {
                        if (v.Identifier.ValueText != varName) continue;
                        if (v.Initializer?.Value is not null && IsCountMinusOne(v.Initializer.Value))
                            return true;
                    }
                }

                if (child is ExpressionStatementSyntax exprStmt &&
                    exprStmt.Expression is AssignmentExpressionSyntax assignment &&
                    assignment.Left is IdentifierNameSyntax assignTarget &&
                    assignTarget.Identifier.ValueText == varName)
                {
                    if (IsCountMinusOne(assignment.Right)) return true;
                }
            }

            return false;
        }

        private static bool IsListOrArrayType(INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Array) return true;

            if (!type.IsGenericType) return false;
            var def = type.ConstructedFrom;
            if (def is null) return false;
            var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
            return fullName == "System.Collections.Generic.List";
        }

        private static void AnalyzeListInsertAtHead(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
            if (memberAccess.Name.Identifier.ValueText != "Insert") return;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 2) return;

            var firstArg = args[0].Expression;
            var constantValue = ctx.SemanticModel.GetConstantValue(firstArg);
            if (!constantValue.HasValue) return;
            if (constantValue.Value is not int index || index != 0) return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;
            if (!IsListOrArrayType(containingType)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleListInsertAtHead, invocation.GetLocation()));
        }

        private static void AnalyzeMethodLevelListContains(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var method = (MethodDeclarationSyntax)ctx.Node;

            var containsCounts = new Dictionary<string, List<InvocationExpressionSyntax>>(StringComparer.Ordinal);
            var variableTypes = new Dictionary<string, (string TypeName, string ElementType)>(StringComparer.Ordinal);

            foreach (var descendant in method.DescendantNodes())
            {
                if (descendant is not InvocationExpressionSyntax invocation) continue;
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
                if (memberAccess.Name.Identifier.ValueText != "Contains") continue;

                var receiverText = memberAccess.Expression?.ToString() ?? "";
                if (string.IsNullOrEmpty(receiverText)) continue;

                var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol is null) continue;

                var containingType = symbol.ContainingType;
                if (containingType is null || !IsListOrArrayType(containingType)) continue;

                var elementType = "T";
                if (containingType.TypeArguments.Length > 0)
                    elementType = containingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                if (!containsCounts.ContainsKey(receiverText))
                {
                    containsCounts[receiverText] = new List<InvocationExpressionSyntax>();
                    variableTypes[receiverText] = ("List", elementType);
                }
                containsCounts[receiverText].Add(invocation);
            }

            const int threshold = 3;
            foreach (var kvp in containsCounts)
            {
                if (kvp.Value.Count < threshold) continue;

                var varName = kvp.Key;
                var (_, elementType) = variableTypes[varName];
                var firstInvocation = kvp.Value[0];

                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleListContainsToHashSet,
                    firstInvocation.GetLocation(),
                    kvp.Value.Count, varName, elementType));
            }
        }

        private static void AnalyzeStaticReadOnlyLinearSearch(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName != "Contains" && methodName != "IndexOf") return;

            var receiverText = memberAccess.Expression?.ToString() ?? "";
            if (string.IsNullOrEmpty(receiverText)) return;

            var receiverExpr = memberAccess.Expression;
            if (receiverExpr is null) return;
            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(receiverExpr);
            if (symbolInfo.Symbol is not IFieldSymbol fieldSymbol) return;

            if (!fieldSymbol.IsStatic || !fieldSymbol.IsReadOnly) return;

            var fieldType = fieldSymbol.Type;
            if (!IsArrayType(fieldType) && !IsListType(fieldType)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleStaticReadOnlyLinearSearch,
                invocation.GetLocation(),
                receiverText, methodName));
        }

        private static void AnalyzeStringToSpan(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName != "Substring" && methodName != "Remove" && methodName != "Split") return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;
            if (containingType.SpecialType != SpecialType.System_String) return;

            if (!AotSafetyHelpers.IsInsideLoop(invocation)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleStringToSpan,
                invocation.GetLocation(),
                methodName));
        }

        private static bool IsArrayType(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Array;
        }

        private static bool IsListType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType) return false;
            if (!namedType.IsGenericType) return false;
            var def = namedType.ConstructedFrom;
            if (def is null) return false;
            var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
            return fullName == "System.Collections.Generic.List";
        }

        private static void AnalyzeRangeQueryBinarySearch(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var loopBody = ctx.Node switch
            {
                ForEachStatementSyntax fe => fe.Statement as BlockSyntax,
                ForStatementSyntax f => f.Statement as BlockSyntax,
                _ => null,
            };
            if (loopBody is null) return;

            foreach (var statement in loopBody.Statements)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (statement is not IfStatementSyntax ifStmt) continue;
                if (!IsRangeCondition(ifStmt.Condition)) continue;

                var conditionText = ifStmt.Condition.ToString();
                if (conditionText.Length > 60)
                    conditionText = conditionText.Substring(0, 57) + "...";

                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleRangeQueryBinarySearch,
                    ifStmt.Condition.GetLocation(),
                    conditionText));
            }
        }

        private static bool IsRangeCondition(ExpressionSyntax? condition)
        {
            if (condition is null) return false;

            if (condition is not BinaryExpressionSyntax binary) return false;
            if (!binary.IsKind(SyntaxKind.LogicalAndExpression)) return false;

            var left = binary.Left;
            var right = binary.Right;

            return IsComparisonWithVariable(left) && IsComparisonWithVariable(right);
        }

        private static bool IsComparisonWithVariable(ExpressionSyntax expr)
        {
            return expr.Kind() switch
            {
                SyntaxKind.GreaterThanOrEqualExpression => true,
                SyntaxKind.LessThanOrEqualExpression => true,
                SyntaxKind.GreaterThanExpression => true,
                SyntaxKind.LessThanExpression => true,
                _ => false,
            };
        }

        private static void AnalyzeForeachToLinq(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not ForEachStatementSyntax foreachStmt) return;
            var body = foreachStmt.Statement as BlockSyntax;
            if (body is null) return;

            if (body.Statements.Count > 3) return;

            if (TryMatchFilterAndAdd(body, out var filterAddDesc))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleForeachToLinq,
                    foreachStmt.ForEachKeyword.GetLocation(),
                    "过滤+收集", filterAddDesc));
                return;
            }

            if (TryMatchAnyPattern(body, out var anyDesc))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleForeachToLinq,
                    foreachStmt.ForEachKeyword.GetLocation(),
                    "存在判断", anyDesc));
                return;
            }

            if (TryMatchAggregation(body, out var aggDesc))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleForeachToLinq,
                    foreachStmt.ForEachKeyword.GetLocation(),
                    "聚合", aggDesc));
                return;
            }
        }

        private static bool TryMatchFilterAndAdd(BlockSyntax body, out string suggestion)
        {
            suggestion = "";
            if (body.Statements.Count != 1) return false;

            if (body.Statements[0] is not IfStatementSyntax ifStmt) return false;

            if (ContainsLinearSearch(ifStmt.Condition)) return false;

            var thenStatement = GetSingleStatement(ifStmt.Statement);
            if (thenStatement is null) return false;

            if (thenStatement is not ExpressionStatementSyntax exprStmt) return false;
            if (exprStmt.Expression is not InvocationExpressionSyntax invocation) return false;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;

            if (memberAccess.Name.Identifier.ValueText == "Add")
            {
                suggestion = ".Where(...).ToList()";
                return true;
            }

            return false;
        }

        private static bool TryMatchAnyPattern(BlockSyntax body, out string suggestion)
        {
            suggestion = "";
            if (body.Statements.Count != 1) return false;

            if (body.Statements[0] is not IfStatementSyntax ifStmt) return false;

            if (ContainsLinearSearch(ifStmt.Condition)) return false;

            var thenStatement = GetSingleStatement(ifStmt.Statement);
            if (thenStatement is null) return false;

            if (thenStatement is not ReturnStatementSyntax returnStmt) return false;
            if (returnStmt.Expression is null) return false;

            var returnText = returnStmt.Expression.ToString().Trim();
            if (returnText == "true")
            {
                suggestion = ".Any(...)";
                return true;
            }

            if (returnText == "false")
            {
                suggestion = ".All(...)";
                return true;
            }

            return false;
        }

        private static bool ContainsLinearSearch(ExpressionSyntax? expr)
        {
            if (expr is null) return false;
            foreach (var node in expr.DescendantNodesAndSelf())
            {
                if (node is InvocationExpressionSyntax invocation &&
                    invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var name = memberAccess.Name.Identifier.ValueText;
                    if (name == "Contains" || name == "IndexOf")
                        return true;
                }
            }
            return false;
        }

        private static StatementSyntax? GetSingleStatement(StatementSyntax thenStatement)
        {
            if (thenStatement is BlockSyntax block)
            {
                if (block.Statements.Count != 1) return null;
                return block.Statements[0];
            }
            return thenStatement;
        }

        private static bool TryMatchAggregation(BlockSyntax body, out string suggestion)
        {
            suggestion = "";
            if (body.Statements.Count != 1) return false;

            if (body.Statements[0] is not ExpressionStatementSyntax exprStmt) return false;

            if (exprStmt.Expression.IsKind(SyntaxKind.AddAssignmentExpression))
            {
                suggestion = ".Sum(...)";
                return true;
            }

            if (exprStmt.Expression.IsKind(SyntaxKind.PostIncrementExpression) ||
                exprStmt.Expression.IsKind(SyntaxKind.PreIncrementExpression))
            {
                suggestion = ".Count(...)";
                return true;
            }

            return false;
        }

        private static void AnalyzeParallelForEach(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not InvocationExpressionSyntax invocation) return;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName != "ForEach" && methodName != "ForEachAsync" && methodName != "For") return;

            if (memberAccess.Expression is not IdentifierNameSyntax className) return;
            if (className.Identifier.ValueText != "Parallel") return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleParallelForEach, invocation.GetLocation()));
        }

        private static void AnalyzeLongLinqChain(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not InvocationExpressionSyntax invocation) return;

            var chainCount = CountChainedCalls(invocation);
            if (chainCount <= 8) return;

            if (invocation.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleLongLinqChain, invocation.GetLocation(), chainCount));
        }

        private static int CountChainedCalls(InvocationExpressionSyntax invocation)
        {
            var count = 1;
            var current = invocation.Expression;

            while (current is MemberAccessExpressionSyntax memberAccess)
            {
                count++;
                if (memberAccess.Expression is InvocationExpressionSyntax innerInvocation)
                {
                    count += CountChainedCalls(innerInvocation) - 1;
                    break;
                }
                if (memberAccess.Expression is MemberAccessExpressionSyntax innerMemberAccess)
                {
                    break;
                }
                break;
            }

            return count;
        }
    }
}
