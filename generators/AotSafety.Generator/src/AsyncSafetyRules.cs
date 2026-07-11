namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncSafetyRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleConsoleReadLine = new(
            "JCC2001",
            "交互输入: Console.ReadLine() 必须包裹 IsInputRedirected 检查",
            "Console.ReadLine() 在输入重定向环境（测试、CI）中会无限阻塞。必须先检查 Console.IsInputRedirected，在非交互模式下使用替代路径。",
            "InteractiveSafety",
            DiagnosticSeverity.Warning,
            true,
            "测试环境中 Console.IsInputRedirected 可能为 false, 但 TestEnvironmentDetector.IsTestEnvironment 为 true. ReadLine 会无限等待导致卡死. 正确模式: if (Console.IsInputRedirected || TestEnvironmentDetector.IsTestEnvironment) { alternative } else { Console.ReadLine(); }.");

        private static readonly DiagnosticDescriptor RuleConsoleReadKey = new(
            "JCC2002",
            "交互输入: Console.ReadKey() 必须包裹 IsInputRedirected 检查",
            "Console.ReadKey() 在输入重定向环境（测试、CI）中会无限阻塞。必须先检查 Console.IsInputRedirected，在非交互模式下使用替代路径。",
            "InteractiveSafety",
            DiagnosticSeverity.Warning,
            true,
            "测试环境中 Console.IsInputRedirected 可能为 false, 但 TestEnvironmentDetector.IsTestEnvironment 为 true. ReadKey 会无限等待导致卡死. 正确模式: if (Console.IsInputRedirected || TestEnvironmentDetector.IsTestEnvironment) { alternative } else { Console.ReadKey(); }.");

        private static readonly DiagnosticDescriptor RuleConsoleRead = new(
            "JCC2003",
            "交互输入: Console.Read() 必须包裹 IsInputRedirected 检查",
            "Console.Read() 在输入重定向环境（测试、CI）中可能阻塞。必须先检查 Console.IsInputRedirected，在非交互模式下使用替代路径。",
            "InteractiveSafety",
            DiagnosticSeverity.Warning,
            true,
            "测试环境中 Console.IsInputRedirected 可能为 false, 但 TestEnvironmentDetector.IsTestEnvironment 为 true. Read 可能阻塞. 正确模式: if (Console.IsInputRedirected || TestEnvironmentDetector.IsTestEnvironment) { alternative } else { Console.Read(); }.");

        private static readonly DiagnosticDescriptor RuleProcessDeadlock = new(
            "JCC3003",
            "Process deadlock: WaitForExitAsync before ReadToEndAsync",
            "Calling '{0}' after WaitForExitAsync may cause deadlock. When child process output exceeds pipe buffer size, WaitForExitAsync blocks waiting for process exit while the process blocks waiting for pipe read, forming a deadlock. Correct pattern: start ReadToEndAsync first, then await WaitForExitAsync.",
            "ProcessSafety",
            DiagnosticSeverity.Warning,
            true,
            "Process pipe buffer is limited. If child process output exceeds buffer size and is not consumed, the child process blocks on write. If the parent process is waiting on WaitForExitAsync, both sides wait on each other causing deadlock. Start ReadToEndAsync before WaitForExitAsync to avoid this.");

        private static readonly DiagnosticDescriptor RuleUnreadStderr = new(
            "JCC3004",
            "Process deadlock: RedirectStandardError is true but stderr is never read",
            "Process has RedirectStandardError=true but StandardError is never consumed. When the child process writes enough to stderr to fill the pipe buffer, it will block, potentially causing deadlock if the parent is waiting on WaitForExitAsync.",
            "ProcessSafety",
            DiagnosticSeverity.Warning,
            true,
            "When RedirectStandardError is true, the stderr pipe buffer must be consumed. If the child process writes enough data to fill the buffer, it blocks on write. If the parent process is waiting on WaitForExitAsync or reading stdout, both sides wait on each other causing deadlock. Either set RedirectStandardError=false or consume stderr with ReadToEndAsync or BeginErrorReadLine.");

        private static readonly DiagnosticDescriptor RuleAsyncVoid = new(
            "JCC3005",
            "异步红线: async void 方法异常无法被捕获",
            "async void 方法的异常会直接炸掉进程，调用者无法捕获。改为 async Task 返回类型。（UI 事件处理器除外）",
            "AsyncSafety",
            DiagnosticSeverity.Error,
            true,
            "async void 方法的异常不会传播到调用者, 而是直接在 SynchronizationContext 上抛出, 导致应用崩溃. 正确做法: 1) 改为 async Task; 2) UI 事件处理器 (如 Button_Click) 是唯一例外; 3) 如果必须 fire-and-forget, 使用 'async Task' + '_ = MethodAsync()' 模式, 配合 CancellationToken 保护.");

        private static readonly DiagnosticDescriptor RuleBlockingAsyncCall = new(
            "JCC3006",
            "异步红线: .Result/.Wait() 阻塞调用可能导致死锁",
            "在异步上下文中调用 '{0}' 会阻塞当前线程等待 Task 完成，极易导致死锁。使用 await 替代。",
            "AsyncSafety",
            DiagnosticSeverity.Warning,
            true,
            ".Result 和 .Wait() 会阻塞当前线程等待 Task 完成. 在有 SynchronizationContext 的环境 (ASP.NET Classic, WPF, WinForms) 中, Task 完成后需要回到原线程, 但原线程被阻塞, 形成死锁. 即使在无 SynchronizationContext 的环境 (ASP.NET Core, 控制台) 中, 也会浪费线程池线程. 正确做法: 始终使用 await. 例外: Main 方法入口和测试方法中可使用 .Result/.Wait().");

        private static readonly DiagnosticDescriptor RuleSequentialAwaitInLoop = new(
            "JCC3007",
            "异步性能: 循环中逐个 await 可考虑改为 Task.WhenAll 并发执行",
            "循环体内逐个 await 异步操作是串行执行，可考虑收集 Task 后用 Task.WhenAll 并发执行. 串行 await 总耗时 = Σ每个操作耗时，并发 await 总耗时 ≈ Max(各操作耗时).",
            "AsyncPerformance",
            DiagnosticSeverity.Info,
            true,
            "Sequential await in loop could use Task.WhenAll for concurrent execution. Serial await total time = sum of each operation, concurrent await total time = max of each operation.");

        private static readonly DiagnosticDescriptor RuleConfigureAwaitFalse = new(
            "JCC3008",
            "异步规范: 库代码 await 必须使用 ConfigureAwait(false)",
            "库代码（lib/ 和 subsystems/）中的 await 缺少 ConfigureAwait(false). 库代码不依赖 SynchronizationContext，省略 ConfigureAwait(false) 会导致不必要的上下文切换和潜在死锁.",
            "AsyncCorrectness",
            DiagnosticSeverity.Warning,
            true,
            "Library code (lib/ and subsystems/) must use ConfigureAwait(false). Host entry (JoinCode) and test code are exempt.");

        private static readonly DiagnosticDescriptor RuleConfigureAwaitTrueForTests = new(
            "JCC3009",
            "异步规范: 测试代码禁止 ConfigureAwait(false)",
            "测试代码中使用了 ConfigureAwait(false). 测试代码依赖 xUnit SynchronizationContext，ConfigureAwait(false) 会导致测试行为不一致. 测试代码中 await 默认即为 ConfigureAwait(true)，无需显式指定.",
            "AsyncCorrectness",
            DiagnosticSeverity.Warning,
            true,
            "Test code must not use ConfigureAwait(false). Default ConfigureAwait(true) is implicit, no need to specify explicitly.");

        private static readonly DiagnosticDescriptor RuleTaskDelayIntInTests = new(
            "JCC3010",
            "测试性能: Task.Delay({0}ms) 真实等待",
            "测试中 Task.Delay({0}ms) 真实等待。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
            "AsyncCorrectness",
            DiagnosticSeverity.Warning,
            true,
            "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.");

        private static readonly DiagnosticDescriptor RuleTaskDelayTimeSpanInTests = new(
            "JCC3011",
            "测试性能: Task.Delay({0}) 真实等待 {1}ms",
            "测试中 Task.Delay({0}) 真实等待 {1}ms。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
            "AsyncCorrectness",
            DiagnosticSeverity.Warning,
            true,
            "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.");

        private static readonly DiagnosticDescriptor RuleTaskDelayUnknownInTests = new(
            "JCC3012",
            "测试性能: Task.Delay 真实等待",
            "测试中 Task.Delay 真实等待。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
            "AsyncCorrectness",
            DiagnosticSeverity.Warning,
            true,
            "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.");

        private static readonly DiagnosticDescriptor RuleEmptyCatchBlock = new(
            "JCC3013",
            "代码规范: 禁止空 catch 块",
            "空 catch 块会隐藏异常，导致难以调试的问题。catch 块必须包含实际处理逻辑（记录日志、重新抛出或执行恢复操作）。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "Empty catch blocks silently swallow exceptions, making bugs impossible to diagnose. Every catch block must contain actual handling logic: logging, rethrowing, or recovery. Exception: catch(OperationCanceledException) in async loops is acceptable when cancellation is the expected exit path.");

        private static readonly HashSet<string> InteractiveInputMethods = new(StringComparer.Ordinal)
        {
            "Console.ReadLine",
            "Console.ReadKey",
            "Console.Read",
            "System.Console.ReadLine",
            "System.Console.ReadKey",
            "System.Console.Read",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                RuleConsoleReadLine, RuleConsoleReadKey, RuleConsoleRead,
                RuleProcessDeadlock, RuleUnreadStderr,
                RuleAsyncVoid, RuleBlockingAsyncCall,
                RuleSequentialAwaitInLoop, RuleConfigureAwaitFalse, RuleConfigureAwaitTrueForTests,
                RuleTaskDelayIntInTests, RuleTaskDelayTimeSpanInTests, RuleTaskDelayUnknownInTests,
                RuleEmptyCatchBlock);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeInteractiveInput, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeProcessDeadlock, SyntaxKind.AwaitExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAsyncVoid, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeBlockingAsyncCall, SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxNodeAction(AnalyzeSequentialAwaitInLoop, SyntaxKind.AwaitExpression);
            context.RegisterSyntaxNodeAction(AnalyzeConfigureAwaitFalse, SyntaxKind.AwaitExpression);
            context.RegisterSyntaxNodeAction(AnalyzeConfigureAwaitTrueForTests, SyntaxKind.AwaitExpression);
            context.RegisterSyntaxNodeAction(AnalyzeTaskDelayInTests, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeEmptyCatchBlock, SyntaxKind.CatchClause);
            context.RegisterSyntaxNodeAction(AnalyzeUnreadStderr, SyntaxKind.ObjectCreationExpression);
        }

        private static void AnalyzeInteractiveInput(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            var fullName = $"{containingType.ContainingNamespace?.ToDisplayString()}.{containingType.Name}.{symbol.Name}";
            if (!InteractiveInputMethods.Contains(fullName)) return;

            if (IsInsideIsInputRedirectedCheck(invocation)) return;

            if (IsInsideIfDebugDirective(invocation)) return;

            var rule = symbol.Name switch
            {
                "ReadLine" => RuleConsoleReadLine,
                "ReadKey" => RuleConsoleReadKey,
                "Read" => RuleConsoleRead,
                _ => RuleConsoleReadLine
            };

            ctx.ReportDiagnostic(Diagnostic.Create(rule, invocation.GetLocation()));
        }

        private static bool IsInsideIsInputRedirectedCheck(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is IfStatementSyntax ifStatement)
                {
                    if (ContainsIsInputRedirectedCheck(ifStatement.Condition))
                    {
                        if (IsInProtectedBranch(node, ifStatement))
                            return true;
                    }
                }
                else if (current is ConditionalExpressionSyntax conditional)
                {
                    if (ContainsIsInputRedirectedCheck(conditional.Condition))
                        return true;
                }

                if (current is BlockSyntax block)
                {
                    if (IsProtectedByEarlyReturnInBlock(node, block))
                        return true;
                }

                current = current.Parent;
            }
            return false;
        }

        private static bool IsInProtectedBranch(SyntaxNode node, IfStatementSyntax ifStatement)
        {
            var condition = ifStatement.Condition;
            var isTopLevelNegated = IsNegatedCondition(condition);
            var containsInnerNegation = ContainsNegatedIsInputRedirected(condition);

            if (isTopLevelNegated)
            {
                if (ifStatement.Statement is not null && IsDescendantOf(node, ifStatement.Statement))
                    return true;
            }
            else if (containsInnerNegation)
            {
                if (ifStatement.Statement is not null && IsDescendantOf(node, ifStatement.Statement))
                    return true;
            }
            else
            {
                if (ifStatement.Else is not null && IsDescendantOf(node, ifStatement.Else))
                    return true;
            }

            return false;
        }

        private static bool ContainsNegatedIsInputRedirected(ExpressionSyntax condition)
        {
            foreach (var descendant in condition.DescendantNodesAndSelf())
            {
                if (descendant is PrefixUnaryExpressionSyntax prefix
                    && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
                {
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

        private static bool IsNegatedCondition(ExpressionSyntax condition)
        {
            if (condition is PrefixUnaryExpressionSyntax prefix && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
                return true;
            return false;
        }

        private static bool IsProtectedByEarlyReturnInBlock(SyntaxNode node, BlockSyntax block)
        {
            var statement = FindAncestorStatement(node);
            if (statement is null) return false;

            var nodeIndex = block.Statements.IndexOf(statement);
            if (nodeIndex < 0) return false;

            for (var i = 0; i < nodeIndex; i++)
            {
                var stmt = block.Statements[i];
                if (stmt is IfStatementSyntax ifStmt && ContainsIsInputRedirectedCheck(ifStmt.Condition))
                {
                    if (IfStatementExitsEarly(ifStmt))
                        return true;
                }
            }

            return false;
        }

        private static StatementSyntax? FindAncestorStatement(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is StatementSyntax statement)
                    return statement;
                current = current.Parent;
            }
            return null;
        }

        private static bool IfStatementExitsEarly(IfStatementSyntax ifStmt)
        {
            return BlockContainsExit(ifStmt.Statement);
        }

        private static bool BlockContainsExit(StatementSyntax statement)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                case ThrowStatementSyntax:
                case BreakStatementSyntax:
                case ContinueStatementSyntax:
                    return true;
                case BlockSyntax block:
                    foreach (var stmt in block.Statements)
                    {
                        if (BlockContainsExit(stmt))
                            return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool IsDescendantOf(SyntaxNode node, SyntaxNode ancestor)
        {
            var current = node;
            while (current is not null)
            {
                if (current == ancestor) return true;
                current = current.Parent;
            }
            return false;
        }

        private static bool ContainsIsInputRedirectedCheck(ExpressionSyntax condition)
        {
            foreach (var descendant in condition.DescendantNodesAndSelf())
            {
                if (descendant is MemberAccessExpressionSyntax memberAccess)
                {
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

        private static bool IsInsideIfDebugDirective(SyntaxNode node)
        {
            var current = node;
            while (current is not null)
            {
                foreach (var trivia in current.GetLeadingTrivia())
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        var text = trivia.ToString();
                        if (text.Contains("DEBUG"))
                            return true;
                    }
                }
                current = current.Parent;
            }

            if (node.Parent is not null)
            {
                foreach (var trivia in node.Parent.GetLeadingTrivia())
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        var text = trivia.ToString();
                        if (text.Contains("DEBUG"))
                            return true;
                    }
                }
            }

            return false;
        }

        private static void AnalyzeProcessDeadlock(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var awaitExpr = (AwaitExpressionSyntax)ctx.Node;

            var invocation = FindMethodInvocation(awaitExpr.Expression, "WaitForExitAsync");
            if (invocation is null) return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;
            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (typeName != "Process") return;

            var processVariableName = GetProcessVariableName(invocation);
            if (processVariableName is null) return;

            var awaitStatement = awaitExpr.Parent;
            while (awaitStatement is InvocationExpressionSyntax or MemberAccessExpressionSyntax)
                awaitStatement = awaitStatement.Parent;
            if (awaitStatement is null) return;

            var parentBlock = awaitStatement.Parent;
            if (parentBlock is null) return;

            var awaitSpan = awaitStatement.Span;
            var foundAwaitStatement = false;
            foreach (var child in parentBlock.ChildNodes())
            {
                if (!foundAwaitStatement)
                {
                    if (child.Span == awaitSpan)
                    {
                        foundAwaitStatement = true;
                    }
                    continue;
                }

                var readToEndCall = FindReadToEndAsyncCall(child, processVariableName);
                if (readToEndCall is not null)
                {
                    var memberAccess = readToEndCall.Expression as MemberAccessExpressionSyntax;
                    var readTarget = memberAccess?.Expression?.ToString() ?? "stream";
                    var desc = $"{readTarget}.ReadToEndAsync()";
                    ctx.ReportDiagnostic(Diagnostic.Create(RuleProcessDeadlock, readToEndCall.GetLocation(), desc));
                }
            }
        }

        private static string? GetProcessVariableName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Expression?.ToString();
            }
            return null;
        }

        private static InvocationExpressionSyntax? FindMethodInvocation(ExpressionSyntax expr, string methodName)
        {
            if (expr is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax directAccess &&
                    directAccess.Name.Identifier.ValueText == methodName)
                {
                    return inv;
                }

                if (inv.Expression is MemberAccessExpressionSyntax chainAccess &&
                    chainAccess.Expression is InvocationExpressionSyntax innerInv)
                {
                    var result = FindMethodInvocation(innerInv, methodName);
                    if (result is not null) return result;
                }
            }

            return null;
        }

        private static InvocationExpressionSyntax? FindReadToEndAsyncCall(SyntaxNode node, string processVariableName)
        {
            foreach (var descendant in node.DescendantNodesAndSelf())
            {
                if (descendant is InvocationExpressionSyntax invocation)
                {
                    var symbolInfo = invocation.Expression;
                    if (symbolInfo is MemberAccessExpressionSyntax memberAccess)
                    {
                        if (memberAccess.Name.Identifier.ValueText == "ReadToEndAsync")
                        {
                            var leftStr = memberAccess.Expression?.ToString() ?? "";
                            if ((leftStr.Contains("StandardOutput") || leftStr.Contains("StandardError")) &&
                                leftStr.StartsWith(processVariableName, StringComparison.Ordinal))
                            {
                                return invocation;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static void AnalyzeAsyncVoid(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not MethodDeclarationSyntax methodDecl) return;

            if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;
            if (methodDecl.ReturnType is not PredefinedTypeSyntax predefined ||
                !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)) return;

            var methodName = methodDecl.Identifier.ValueText;
            if (IsUiEventHandler(methodName)) return;

            if (IsTimerCallbackPattern(methodName)) return;

            if (methodDecl.AttributeLists.Any(al =>
                al.Attributes.Any(a =>
                    a.Name.ToString().Contains("EventHandler", StringComparison.Ordinal) ||
                    a.Name.ToString().Contains("Handles", StringComparison.Ordinal))))
                return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleAsyncVoid, methodDecl.ReturnType.GetLocation()));
        }

        private static bool IsTimerCallbackPattern(string methodName)
        {
            var callbackPrefixes = new[] { "Process", "Handle", "OnTimer", "TimerCallback" };
            foreach (var prefix in callbackPrefixes)
            {
                if (methodName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsUiEventHandler(string methodName)
        {
            var eventSuffixes = new[]
            {
                "_Click", "_Changed", "_Loaded", "_Closing", "_Closed",
                "_Activated", "_Deactivated", "_GotFocus", "_LostFocus",
                "_KeyDown", "_KeyUp", "_KeyPress", "_MouseEnter", "_MouseLeave",
                "_SelectedIndexChanged", "_TextChanged", "_CheckedChanged",
                "OnClick", "OnChanged", "OnLoaded", "OnClosing", "OnClosed",
            };

            foreach (var suffix in eventSuffixes)
            {
                if (methodName.EndsWith(suffix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AnalyzeBlockingAsyncCall(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;

            var memberName = memberAccess.Name.Identifier.ValueText;

            if (memberName != "Result" && memberName != "Wait") return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(memberAccess.Expression, ctx.CancellationToken);
            var symbol = symbolInfo.Symbol;
            if (symbol is null) return;

            var type = symbol switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol prop => prop.Type,
                IParameterSymbol param => param.Type,
                IMethodSymbol method => method.ReturnType,
                _ => null,
            };

            if (type is null) return;

            var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isTaskType = typeName.StartsWith("global::System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                             typeName.StartsWith("global::System.Threading.Tasks.ValueTask", StringComparison.Ordinal);

            if (!isTaskType) return;

            if (IsInsideMainMethod(memberAccess)) return;
            if (AotSafetyHelpers.IsInsideTestMethod(memberAccess)) return;

            if (IsInsideConstructor(memberAccess)) return;

            if (IsInsideSyncMethod(memberAccess)) return;

            if (IsInsideDisposeMethod(memberAccess)) return;

            var callText = memberName == "Result" ? ".Result" : ".Wait()";
            ctx.ReportDiagnostic(Diagnostic.Create(RuleBlockingAsyncCall, memberAccess.Name.GetLocation(), callText));
        }

        private static bool IsInsideMainMethod(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax methodDecl &&
                    methodDecl.Identifier.ValueText == "Main")
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private static bool IsInsideConstructor(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is ConstructorDeclarationSyntax)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private static bool IsInsideSyncMethod(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax methodDecl)
                {
                    if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
                        return true;
                    return false;
                }
                if (current is LambdaExpressionSyntax lambda)
                {
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

        private static bool IsInsideDisposeMethod(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax methodDecl)
                {
                    var name = methodDecl.Identifier.ValueText;
                    if (name is "Dispose" or "DisposeAsync")
                        return true;
                    return false;
                }
                current = current.Parent;
            }
            return false;
        }

        private static void AnalyzeSequentialAwaitInLoop(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;

            if (!AotSafetyHelpers.IsInsideLoop(awaitExpr)) return;

            if (awaitExpr.Parent is not ExpressionStatementSyntax) return;

            var loop = FindInnermostLoop(awaitExpr);
            if (loop is null) return;

            if (loop is WhileStatementSyntax or DoStatementSyntax) return;

            if (loop is ForStatementSyntax forStmt)
            {
                if (ContainsCancellationTokenCondition(forStmt.Condition)) return;
            }

            if (LoopHasEarlyExit(loop)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleSequentialAwaitInLoop, awaitExpr.GetLocation()));
        }

        private static SyntaxNode? FindInnermostLoop(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is ForEachStatementSyntax or ForEachVariableStatementSyntax or
                    ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                    return current;
                current = current.Parent;
            }
            return null;
        }

        private static bool ContainsCancellationTokenCondition(SyntaxNode? condition)
        {
            if (condition is null) return false;
            foreach (var desc in condition.DescendantNodesAndSelf())
            {
                if (desc is IdentifierNameSyntax identifier)
                {
                    var name = identifier.Identifier.ValueText;
                    if (name.Contains("Cancellation", StringComparison.Ordinal) ||
                        name.Contains("cancellationToken", StringComparison.Ordinal) ||
                        name == "ct" || name == "token")
                        return true;
                }

                if (desc is MemberAccessExpressionSyntax memberAccess)
                {
                    var name = memberAccess.Name.Identifier.ValueText;
                    if (name.Contains("Cancellation", StringComparison.Ordinal) ||
                        name == "IsRunning" || name == "IsConnected")
                        return true;
                }
            }
            return false;
        }

        private static bool LoopHasEarlyExit(SyntaxNode loop)
        {
            SyntaxNode body = loop switch
            {
                ForEachStatementSyntax f => f.Statement,
                ForEachVariableStatementSyntax f => f.Statement,
                ForStatementSyntax f => f.Statement,
                WhileStatementSyntax w => w.Statement,
                DoStatementSyntax d => d.Statement,
                _ => throw new InvalidOperationException(),
            };

            foreach (var desc in body.DescendantNodes())
            {
                if (desc is BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax)
                    return true;
            }
            return false;
        }

        private static void AnalyzeConfigureAwaitFalse(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;

            var filePath = awaitExpr.SyntaxTree.FilePath;

            if (string.IsNullOrEmpty(filePath)) return;
            if (filePath[0] == '/') return;

            var isLibCode = filePath.Contains("\\lib\\", StringComparison.Ordinal) ||
                            filePath.Contains("/lib/", StringComparison.Ordinal);
            var isSubsystemCode = filePath.Contains("\\subsystems\\", StringComparison.Ordinal) ||
                                  filePath.Contains("/subsystems/", StringComparison.Ordinal);
            if (!isLibCode && !isSubsystemCode) return;

            if (HasConfigureAwaitFalse(awaitExpr)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleConfigureAwaitFalse, awaitExpr.GetLocation()));
        }

        private static bool HasConfigureAwaitFalse(AwaitExpressionSyntax awaitExpr)
        {
            if (awaitExpr.Expression is InvocationExpressionSyntax configureAwaitInvocation)
            {
                if (configureAwaitInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == "ConfigureAwait")
                {
                    var args = configureAwaitInvocation.ArgumentList.Arguments;
                    if (args.Count == 1)
                    {
                        var arg = args[0].Expression;
                        if (arg is LiteralExpressionSyntax literal &&
                            literal.Token.IsKind(SyntaxKind.FalseKeyword))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void AnalyzeConfigureAwaitTrueForTests(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;

            var filePath = awaitExpr.SyntaxTree.FilePath;

            if (string.IsNullOrEmpty(filePath)) return;
            if (filePath[0] == '/') return;

            var isTestCode = filePath.Contains("\\tests\\", StringComparison.Ordinal) ||
                             filePath.Contains("/tests/", StringComparison.Ordinal);
            if (!isTestCode) return;

            var isTestingCommon = filePath.Contains("\\Testing.Common\\", StringComparison.Ordinal) ||
                                  filePath.Contains("/Testing.Common/", StringComparison.Ordinal);
            if (isTestingCommon) return;

            var isMockServer = filePath.Contains("\\MockServers\\", StringComparison.Ordinal) ||
                               filePath.Contains("/MockServers/", StringComparison.Ordinal);
            if (isMockServer) return;

            if (IsTaskYield(awaitExpr)) return;

            if (HasConfigureAwaitFalse(awaitExpr))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleConfigureAwaitTrueForTests, awaitExpr.GetLocation()));
            }
        }

        private static bool IsTaskYield(AwaitExpressionSyntax awaitExpr)
        {
            if (awaitExpr.Expression is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == "Yield")
                {
                    return true;
                }
            }

            return false;
        }

        private static void AnalyzeTaskDelayInTests(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            if (containingType.Name != "Task" || symbol.Name != "Delay") return;

            var filePath = invocation.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(filePath)) return;
            if (filePath[0] == '/') return;

            var isTestCode = filePath.Contains("\\tests\\", StringComparison.Ordinal) ||
                             filePath.Contains("/tests/", StringComparison.Ordinal);
            if (!isTestCode) return;

            var isTestingCommon = filePath.Contains("\\Testing.Common\\", StringComparison.Ordinal) ||
                                  filePath.Contains("/Testing.Common/", StringComparison.Ordinal);
            if (isTestingCommon) return;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count > 0)
            {
                var firstArg = args[0].Expression;
                if (firstArg is LiteralExpressionSyntax literal &&
                    literal.Token.Value is int delayMs &&
                    delayMs <= 1)
                {
                    return;
                }

                if (symbol.Parameters.Length > 0)
                {
                    var firstParamType = symbol.Parameters[0].Type;
                    if (firstParamType.SpecialType == SpecialType.System_Int32)
                    {
                        if (firstArg is LiteralExpressionSyntax intLiteral &&
                            intLiteral.Token.Value is int ms)
                        {
                            ctx.ReportDiagnostic(Diagnostic.Create(
                                RuleTaskDelayIntInTests,
                                invocation.GetLocation(),
                                ms.ToString()));
                            return;
                        }

                        ctx.ReportDiagnostic(Diagnostic.Create(
                            RuleTaskDelayUnknownInTests,
                            invocation.GetLocation()));
                        return;
                    }
                    else if (firstParamType.Name == "TimeSpan")
                    {
                        if (firstArg is InvocationExpressionSyntax tsInvocation)
                        {
                            var tsSymbol = ctx.SemanticModel.GetSymbolInfo(tsInvocation).Symbol as IMethodSymbol;
                            if (tsSymbol?.Name == "FromMilliseconds" &&
                                tsInvocation.ArgumentList.Arguments.Count > 0 &&
                                tsInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax msLiteral &&
                                msLiteral.Token.Value is int tsMs)
                            {
                                ctx.ReportDiagnostic(Diagnostic.Create(
                                    RuleTaskDelayTimeSpanInTests,
                                    invocation.GetLocation(),
                                    $"TimeSpan.FromMilliseconds({tsMs})",
                                    tsMs.ToString()));
                                return;
                            }

                            if (tsSymbol?.Name == "FromSeconds" &&
                                tsInvocation.ArgumentList.Arguments.Count > 0 &&
                                tsInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax secLiteral)
                            {
                                var secValue = secLiteral.Token.Value;
                                var secMs = secValue switch
                                {
                                    int s => s * 1000,
                                    double d => (int)(d * 1000),
                                    float f => (int)(f * 1000),
                                    _ => -1
                                };
                                if (secMs > 0)
                                {
                                    ctx.ReportDiagnostic(Diagnostic.Create(
                                        RuleTaskDelayTimeSpanInTests,
                                        invocation.GetLocation(),
                                        $"TimeSpan.FromSeconds({secValue})",
                                        secMs.ToString()));
                                    return;
                                }
                            }
                        }

                        ctx.ReportDiagnostic(Diagnostic.Create(
                            RuleTaskDelayUnknownInTests,
                            invocation.GetLocation()));
                        return;
                    }
                }

                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleTaskDelayUnknownInTests,
                    invocation.GetLocation()));
            }
            else
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleTaskDelayUnknownInTests,
                    invocation.GetLocation()));
            }
        }

        private static void AnalyzeEmptyCatchBlock(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var catchClause = (CatchClauseSyntax)ctx.Node;
            var block = catchClause.Block;
            if (block is null) return;

            if (block.Statements.Count > 0) return;

            if (catchClause.Declaration is not null)
            {
                var exceptionType = ctx.SemanticModel.GetTypeInfo(catchClause.Declaration.Type, ctx.CancellationToken).Type;
                if (exceptionType is not null)
                {
                    var name = exceptionType.Name;
                    if (name is "OperationCanceledException" or "TaskCanceledException")
                        return;
                }
            }

            ctx.ReportDiagnostic(Diagnostic.Create(RuleEmptyCatchBlock, catchClause.CatchKeyword.GetLocation()));
        }

        private static void AnalyzeUnreadStderr(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var objectCreation = (ObjectCreationExpressionSyntax)ctx.Node;

            var typeSymbol = ctx.SemanticModel.GetTypeInfo(objectCreation.Type).Type;
            if (typeSymbol is not null)
            {
                var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (typeName != "ProcessStartInfo") return;
            }
            else
            {
                var typeNameSyntax = objectCreation.Type.ToString();
                if (typeNameSyntax != "ProcessStartInfo") return;
            }

            if (!HasRedirectStandardErrorTrue(objectCreation, ctx)) return;

            var enclosingBlock = FindEnclosingClassBlock(objectCreation);
            if (enclosingBlock is null) return;

            if (HasStandardErrorConsumption(enclosingBlock)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleUnreadStderr, objectCreation.GetLocation()));
        }

        private static bool HasRedirectStandardErrorTrue(ObjectCreationExpressionSyntax objectCreation, SyntaxNodeAnalysisContext ctx)
        {
            if (objectCreation.Initializer is not null)
            {
                foreach (var initializer in objectCreation.Initializer.Expressions)
                {
                    if (initializer is AssignmentExpressionSyntax assignment)
                    {
                        var left = assignment.Left.ToString().Replace(" ", "");
                        if (left == "RedirectStandardError")
                        {
                            var right = assignment.Right.ToString().Trim();
                            if (right == "true") return true;
                        }
                    }
                }
            }

            var variableName = GetProcessStartInfoVariableName(objectCreation);
            if (variableName is not null)
            {
                var enclosingBlock = FindEnclosingClassBlock(objectCreation);
                if (enclosingBlock is not null)
                {
                    foreach (var descendant in enclosingBlock.DescendantNodes())
                    {
                        if (descendant is AssignmentExpressionSyntax assignment)
                        {
                            var left = assignment.Left.ToString().Replace(" ", "");
                            if (left == $"{variableName}.RedirectStandardError")
                            {
                                var right = assignment.Right.ToString().Trim();
                                if (right == "true") return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static string? GetProcessStartInfoVariableName(ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.Parent is EqualsValueClauseSyntax equalsValue &&
                equalsValue.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                return variableDeclarator.Identifier.ValueText;
            }

            return null;
        }

        private static SyntaxNode? FindEnclosingClassBlock(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)
                {
                    return current;
                }
                current = current.Parent;
            }
            return null;
        }

        private static bool HasStandardErrorConsumption(SyntaxNode block)
        {
            foreach (var descendant in block.DescendantNodes())
            {
                if (descendant is MemberAccessExpressionSyntax memberAccess)
                {
                    var name = memberAccess.Name.Identifier.ValueText;
                    if (name == "StandardError") return true;
                }

                if (descendant is InvocationExpressionSyntax invocation)
                {
                    if (invocation.Expression is MemberAccessExpressionSyntax invMemberAccess)
                    {
                        if (invMemberAccess.Name.Identifier.ValueText == "BeginErrorReadLine") return true;
                    }
                }

                if (descendant is AssignmentExpressionSyntax eventAssignment)
                {
                    var left = eventAssignment.Left.ToString().Replace(" ", "");
                    if (left.Contains("ErrorDataReceived")) return true;
                }
            }
            return false;
        }
    }
}
