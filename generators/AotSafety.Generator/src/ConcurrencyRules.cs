namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ConcurrencyRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleFireAndForgetNoCancellationToken = new(
            "JCC3001",
            "即发即忘: 异步调用缺少 CancellationToken 保护",
            "即发即忘调用 '{0}' 未传递 CancellationToken。异步操作在 Dispose 后可能继续执行导致 ObjectDisposedException。应传递 _disposeCts.Token 并添加 WaitAsync 超时保护。",
            "FireAndForgetSafety",
            DiagnosticSeverity.Warning,
            true,
            "即发即忘模式中如果异步方法接受 CancellationToken 参数但未传入, Dispose 后异步操作仍会继续执行, 可能访问已释放的资源. 正确模式: 添加 _disposeCts 字段, 传递 _disposeCts.Token, 添加 WaitAsync 超时, Dispose 中 _disposeCts.Cancel(), catch (OperationCanceledException) 静默处理.");

        private static readonly DiagnosticDescriptor RuleTaskRunNoCancellationToken = new(
            "JCC3002",
            "即发即忘: Task.Run 缺少 CancellationToken 参数",
            "Task.Run 调用未传递 CancellationToken。任务在 Dispose 后可能继续执行导致异常。应传递 _disposeCts.Token 作为第二个参数。",
            "FireAndForgetSafety",
            DiagnosticSeverity.Warning,
            true,
            "Task.Run 在即发即忘模式中必须传递 CancellationToken, 否则 Dispose 后任务无法取消.");

        private static readonly DiagnosticDescriptor RuleLockInAsyncMethod = new(
            "JCC4001",
            "死锁风险: lock 语句在 async 方法中使用",
            "lock 语句在 async 方法 '{0}' 中使用. lock 不支持 await，如果持有锁的线程需要 await 另一个需要同一锁的操作，将导致死锁. 应使用 SemaphoreSlim(1, 1) 替代.",
            "DeadlockSafety",
            DiagnosticSeverity.Warning,
            true,
            "lock 语句在 async 上下文中是危险的: 1) lock 块内不能使用 await; 2) 如果持有锁的代码路径间接 await 了需要同一锁的操作，会死锁; 3) SemaphoreSlim.WaitAsync 是异步兼容的替代方案.");

        private static readonly DiagnosticDescriptor RuleSemaphoreSlimWaitNoTimeout = new(
            "JCC4002",
            "死锁风险: SemaphoreSlim.Wait() 无超时保护",
            "SemaphoreSlim.Wait() 调用无超时参数. 如果信号量永远不释放，调用将无限阻塞. 应使用 WaitAsync(timeout) 或 Wait(timeout) 并处理超时.",
            "DeadlockSafety",
            DiagnosticSeverity.Warning,
            true,
            "无超时的 Wait 调用可能在信号量持有者异常退出时永远阻塞.正确模式: await semaphore.WaitAsync(timeout).ConfigureAwait(false) 或 semaphore.Wait(timeout)，并处理 OperationCanceledException/TimeoutException.");

        private static readonly DiagnosticDescriptor RuleLockWithGetAwaiter = new(
            "JCC4003",
            "死锁风险: lock 语句内调用 GetAwaiter().GetResult()",
            "lock 语句内调用 '{0}.GetAwaiter().GetResult()' 可能导致死锁. 如果异步操作需要回到被 lock 阻塞的线程，将形成死锁. 应使用 SemaphoreSlim.WaitAsync() 替代 lock + GetAwaiter 模式.",
            "DeadlockSafety",
            DiagnosticSeverity.Warning,
            true,
            "lock 持有线程独占锁，GetAwaiter().GetResult() 阻塞当前线程等待异步操作完成.如果异步操作需要获取同一锁或回到被阻塞的线程(SynchronizationContext)，将形成死锁.正确模式: SemaphoreSlim(1,1) + WaitAsync + try/finally Release.");

        private static readonly DiagnosticDescriptor RuleLockAndSemaphoreSlimOnSameState = new(
            "JCC4004",
            "死锁风险: lock 和 SemaphoreSlim 保护同一状态（竞态条件）",
            "字段 '{0}' 同时被 lock 语句和 SemaphoreSlim '{1}' 保护，存在竞态条件. 应统一使用 SemaphoreSlim 替代 lock，确保异步和同步路径使用同一把锁.",
            "DeadlockSafety",
            DiagnosticSeverity.Warning,
            true,
            "lock 和 SemaphoreSlim 是两种不同的锁机制，同时保护同一状态时: 1) lock 是线程独占锁，SemaphoreSlim 是信号量; 2) 两者互不感知，无法保证互斥; 3) 异步方法通过 SemaphoreSlim 获取锁，同步方法通过 lock 获取锁，两者可以同时进入临界区.正确做法: 统一使用 SemaphoreSlim(1,1)，同步路径用 Wait(0)，异步路径用 WaitAsync().");

        private static readonly DiagnosticDescriptor RuleSemaphoreSlimNotDisposed = new(
            "JCC4005",
            "资源泄漏: SemaphoreSlim 字段 '{0}' 未在 Dispose 中释放",
            "SemaphoreSlim 字段 '{0}' 未在 Dispose/DisposeAsync 方法中调用 Dispose(). SemaphoreSlim 持有内核句柄，未释放将导致句柄泄漏.",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "SemaphoreSlim 持有 ManualResetEvent 内核句柄.如果类实现了 IDisposable 但未在 Dispose 中释放 SemaphoreSlim 字段，将导致内核句柄泄漏.正确模式: 在 Dispose() 中调用 _semaphore.Dispose().如果类有多个 SemaphoreSlim，逐一释放.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleConcurrentDictSemaphoreSlimNotDisposed = new(
            "JCC4006",
            "资源泄漏: ConcurrentDictionary<SemaphoreSlim> 字段 '{0}' 未逐个 Dispose",
            "ConcurrentDictionary<SemaphoreSlim> 字段 '{0}' 在 Dispose 中仅调用 Clear() 而未逐个 Dispose Value。SemaphoreSlim 持有内核句柄，仅 Clear 会导致句柄泄漏。应先 foreach 逐个 kvp.Value.Dispose()，再 Clear()。",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "ConcurrentDictionary.Clear() 仅移除引用，不调用 Value 的 Dispose.SemaphoreSlim 持有 ManualResetEvent 内核句柄，未释放将泄漏.正确模式: foreach (var kvp in _dict) { kvp.Value.Dispose(); } _dict.Clear(); .",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleThreadSleep = new(
            "JCC5001",
            "性能: Thread.Sleep 阻塞线程",
            "Thread.Sleep 在非测试代码中使用，会阻塞当前线程。应使用 Task.Delay 替代，不阻塞线程池线程。",
            "PerformanceSafety",
            DiagnosticSeverity.Warning,
            true,
            "Thread.Sleep 阻塞当前线程，在异步应用中浪费线程池资源.Task.Delay 是异步替代方案，不阻塞线程.测试代码中可以使用 Thread.Sleep 但应标记为 [Fact(Timeout = N)] 防止无限等待.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                RuleFireAndForgetNoCancellationToken, RuleTaskRunNoCancellationToken,
                RuleLockInAsyncMethod, RuleSemaphoreSlimWaitNoTimeout,
                RuleLockWithGetAwaiter, RuleLockAndSemaphoreSlimOnSameState,
                RuleSemaphoreSlimNotDisposed, RuleConcurrentDictSemaphoreSlimNotDisposed,
                RuleThreadSleep);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeFireAndForget, SyntaxKind.ExpressionStatement);
            context.RegisterSyntaxNodeAction(AnalyzeLockInAsyncMethod, SyntaxKind.LockStatement);
            context.RegisterSyntaxNodeAction(AnalyzeSemaphoreSlimWaitNoTimeout, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeLockWithGetAwaiter, SyntaxKind.LockStatement);
            context.RegisterSyntaxNodeAction(AnalyzeLockAndSemaphoreSlimOnSameState, SyntaxKind.LockStatement);
            context.RegisterCompilationStartAction(AnalyzeSemaphoreSlimNotDisposed);
            context.RegisterCompilationStartAction(AnalyzeConcurrentDictSemaphoreSlimNotDisposed);
            context.RegisterSyntaxNodeAction(AnalyzeThreadSleep, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeFireAndForget(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var exprStatement = (ExpressionStatementSyntax)ctx.Node;

            if (exprStatement.Expression is not AssignmentExpressionSyntax assignment)
                return;
            if (!assignment.Left.IsKind(SyntaxKind.IdentifierName))
                return;
            var leftIdentifier = (IdentifierNameSyntax)assignment.Left;
            if (leftIdentifier.Identifier.ValueText != "_")
                return;

            var rightExpr = assignment.Right;

            if (rightExpr is InvocationExpressionSyntax invocation)
            {
                var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol is null) return;

                var containingType = symbol.ContainingType;
                if (containingType is null) return;

                var typeName = containingType.Name;
                var methodName = symbol.Name;

                if (typeName == "Task" && methodName == "Run")
                {
                    if (!HasCancellationTokenArgument(ctx, invocation, symbol))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleTaskRunNoCancellationToken, invocation.GetLocation()));
                    }
                    return;
                }

                if (IsAsyncMethod(symbol))
                {
                    if (!HasCancellationTokenArgument(ctx, invocation, symbol))
                    {
                        var displayStr = $"{typeName}.{methodName}";
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleFireAndForgetNoCancellationToken, invocation.GetLocation(), displayStr));
                    }
                    return;
                }
            }

            var innerInvocation = FindInnermostInvocation(rightExpr);
            if (innerInvocation is not null)
            {
                var symbol = ctx.SemanticModel.GetSymbolInfo(innerInvocation).Symbol as IMethodSymbol;
                if (symbol is null) return;

                var containingType = symbol.ContainingType;
                if (containingType is null) return;

                if (containingType.Name == "Task" && symbol.Name == "Run")
                {
                    if (!HasCancellationTokenArgument(ctx, innerInvocation, symbol))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleTaskRunNoCancellationToken, innerInvocation.GetLocation()));
                    }
                    return;
                }

                if (IsAsyncMethod(symbol) && !HasCancellationTokenArgument(ctx, innerInvocation, symbol))
                {
                    var displayStr = $"{containingType.Name}.{symbol.Name}";
                    ctx.ReportDiagnostic(Diagnostic.Create(RuleFireAndForgetNoCancellationToken, innerInvocation.GetLocation(), displayStr));
                }
            }
        }

        private static bool IsAsyncMethod(IMethodSymbol method)
        {
            var returnType = method.ReturnType;
            if (returnType is null) return false;

            var typeName = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return typeName.StartsWith("Task", StringComparison.Ordinal) ||
                   typeName.StartsWith("ValueTask", StringComparison.Ordinal);
        }

        private static bool HasCancellationTokenArgument(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax invocation, IMethodSymbol method)
        {
            var hasCancellationTokenParam = method.Parameters.Any(p =>
            {
                var paramType = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return paramType == "CancellationToken";
            });

            if (!hasCancellationTokenParam) return true;

            var arguments = invocation.ArgumentList.Arguments;
            foreach (var arg in arguments)
            {
                var argType = ctx.SemanticModel.GetTypeInfo(arg.Expression).Type;
                if (argType is not null)
                {
                    var argTypeName = argType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    if (argTypeName == "CancellationToken")
                        return true;
                }
            }

            return false;
        }

        private static InvocationExpressionSyntax? FindInnermostInvocation(ExpressionSyntax expr)
        {
            if (expr is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Expression is InvocationExpressionSyntax innerInv)
                    {
                        return FindInnermostInvocation(innerInv) ?? innerInv;
                    }
                }
                return inv;
            }
            return null;
        }

        private static void AnalyzeLockInAsyncMethod(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var lockStatement = (LockStatementSyntax)ctx.Node;

            var enclosingMethod = AotSafetyHelpers.FindEnclosingMethodDeclaration(lockStatement);
            if (enclosingMethod is null) return;

            if (!enclosingMethod.Modifiers.Any(m => m.ValueText == "async")) return;

            var methodName = enclosingMethod.Identifier.ValueText;
            ctx.ReportDiagnostic(Diagnostic.Create(RuleLockInAsyncMethod, lockStatement.GetLocation(), methodName));
        }

        private static void AnalyzeSemaphoreSlimWaitNoTimeout(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName != "Wait" && methodName != "WaitAsync") return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;
            if (containingType.Name != "SemaphoreSlim") return;

            if (symbol.Parameters.Length == 0) return;

            var hasTimeoutOrCancellationParam = symbol.Parameters.Any(p =>
            {
                var paramTypeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return paramTypeName == "int" || paramTypeName == "TimeSpan" || paramTypeName == "CancellationToken";
            });

            if (!hasTimeoutOrCancellationParam)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleSemaphoreSlimWaitNoTimeout, invocation.GetLocation()));
            }
        }

        private static void AnalyzeLockWithGetAwaiter(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var lockStatement = (LockStatementSyntax)ctx.Node;

            var getAwaiterInvocations = lockStatement.Statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => IsGetAwaiterGetResultPattern(inv, ctx.SemanticModel));

            foreach (var invocation in getAwaiterInvocations)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var exprText = invocation.Expression.ToString();
                ctx.ReportDiagnostic(Diagnostic.Create(RuleLockWithGetAwaiter, invocation.GetLocation(), exprText));
            }
        }

        private static bool IsGetAwaiterGetResultPattern(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax outerAccess) return false;
            if (outerAccess.Name.Identifier.ValueText != "GetResult") return false;

            if (outerAccess.Expression is not InvocationExpressionSyntax innerInvocation) return false;
            if (innerInvocation.Expression is not MemberAccessExpressionSyntax innerAccess) return false;
            if (innerAccess.Name.Identifier.ValueText != "GetAwaiter") return false;

            return true;
        }

        private static void AnalyzeLockAndSemaphoreSlimOnSameState(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var lockStatement = (LockStatementSyntax)ctx.Node;

            var lockExpression = lockStatement.Expression;
            if (lockExpression is null) return;

            var typeDecl = AotSafetyHelpers.FindEnclosingTypeDeclaration(lockStatement);
            if (typeDecl is null) return;

            var lockFieldName = GetIdentifierName(lockExpression);
            if (lockFieldName is null) return;

            if (!lockFieldName.StartsWith("_", StringComparison.Ordinal) && !IsThisMemberAccess(lockExpression))
                return;

            var semaphoreFields = typeDecl.Members
                .OfType<FieldDeclarationSyntax>()
                .Where(f => IsSemaphoreSlimField(f, ctx.SemanticModel))
                .ToList();

            if (semaphoreFields.Count == 0) return;

            foreach (var semField in semaphoreFields)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var semFieldName = semField.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText;
                if (semFieldName is null) continue;

                if (lockFieldName == semFieldName) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(RuleLockAndSemaphoreSlimOnSameState,
                    lockStatement.GetLocation(), lockFieldName, semFieldName));

                return;
            }
        }

        private static bool IsThisMemberAccess(ExpressionSyntax expression)
        {
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is ThisExpressionSyntax)
                return true;
            return false;
        }

        private static string? GetIdentifierName(ExpressionSyntax expression)
        {
            if (expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText;
            if (expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.ValueText;
            return null;
        }

        private static bool IsSemaphoreSlimField(FieldDeclarationSyntax fieldDecl, SemanticModel semanticModel)
        {
            var typeInfo = semanticModel.GetTypeInfo(fieldDecl.Declaration.Type);
            return typeInfo.Type?.Name == "SemaphoreSlim";
        }

        private static void AnalyzeSemaphoreSlimNotDisposed(CompilationStartAnalysisContext context)
        {
            var semaphoreSlimFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

                var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type;
                if (fieldType is null) return;
                if (fieldType.Name != "SemaphoreSlim") return;

                foreach (var variable in fieldDecl.Declaration.Variables)
                {
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                    if (symbol is null) continue;

                    if (symbol.IsStatic) continue;

                    semaphoreSlimFields.TryAdd(symbol, variable.Identifier.GetLocation());
                }
            }, SyntaxKind.FieldDeclaration);

            context.RegisterCompilationEndAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                foreach (var kvp in semaphoreSlimFields)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    var field = kvp.Key;
                    var location = kvp.Value;

                    var containingType = field.ContainingType;
                    if (containingType is null) continue;

                    var implementsDisposable = containingType.AllInterfaces.Any(i =>
                        i.Name == "IDisposable" &&
                        i.ContainingNamespace?.ToDisplayString() == "System");

                    if (!implementsDisposable) continue;

                    var disposedInDispose = IsFieldDisposedInMethod(containingType, field.Name, "Dispose") ||
                                            IsFieldDisposedInMethod(containingType, field.Name, "DisposeAsync");

                    if (!disposedInDispose)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleSemaphoreSlimNotDisposed, location, field.Name));
                    }
                }
            });
        }

        private static void AnalyzeConcurrentDictSemaphoreSlimNotDisposed(CompilationStartAnalysisContext context)
        {
            var concurrentDictFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

                var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type as INamedTypeSymbol;
                if (fieldType is null) return;

                if (fieldType.Name != "ConcurrentDictionary") return;

                if (fieldType.TypeArguments.Length != 2) return;
                if (fieldType.TypeArguments[1].Name != "SemaphoreSlim") return;

                foreach (var variable in fieldDecl.Declaration.Variables)
                {
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                    if (symbol is null) continue;

                    if (symbol.IsStatic) continue;

                    concurrentDictFields.TryAdd(symbol, variable.Identifier.GetLocation());
                }
            }, SyntaxKind.FieldDeclaration);

            context.RegisterCompilationEndAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                foreach (var kvp in concurrentDictFields)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    var field = kvp.Key;
                    var location = kvp.Value;

                    var containingType = field.ContainingType;
                    if (containingType is null) continue;

                    var implementsDisposable = containingType.AllInterfaces.Any(i =>
                        i.Name == "IDisposable" &&
                        i.ContainingNamespace?.ToDisplayString() == "System");

                    if (!implementsDisposable) continue;

                    var hasForEachDispose = HasForEachDisposeInMethod(containingType, field.Name, "Dispose") ||
                                            HasForEachDisposeInMethod(containingType, field.Name, "DisposeAsync");

                    if (!hasForEachDispose)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleConcurrentDictSemaphoreSlimNotDisposed, location, field.Name));
                    }
                }
            });
        }

        private static bool IsFieldDisposedInMethod(INamedTypeSymbol type, string fieldName, string methodName)
        {
            foreach (var refDecl in type.DeclaringSyntaxReferences)
            {
                var syntax = refDecl.GetSyntax();
                if (syntax is not TypeDeclarationSyntax typeDecl) continue;

                var disposeMethods = typeDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.ValueText == methodName);

                foreach (var method in disposeMethods)
                {
                    var hasFieldReference = false;

                    if (method.Body is not null)
                    {
                        hasFieldReference = method.Body.DescendantNodes()
                            .OfType<IdentifierNameSyntax>()
                            .Any(id => id.Identifier.ValueText == fieldName);
                    }
                    else if (method.ExpressionBody is not null)
                    {
                        hasFieldReference = method.ExpressionBody.Expression.DescendantNodesAndSelf()
                            .OfType<IdentifierNameSyntax>()
                            .Any(id => id.Identifier.ValueText == fieldName);
                    }

                    if (hasFieldReference) return true;
                }
            }

            return false;
        }

        private static bool HasForEachDisposeInMethod(INamedTypeSymbol type, string fieldName, string methodName)
        {
            return IsFieldDisposedInMethod(type, fieldName, methodName);
        }

        private static void AnalyzeThreadSleep(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            if (containingType.Name != "Thread" || symbol.Name != "Sleep") return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleThreadSleep, invocation.GetLocation()));
        }
    }
}
