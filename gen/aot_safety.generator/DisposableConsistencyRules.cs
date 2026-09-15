namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DisposableConsistencyRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleDualDisposable = new(
            "JCC9102",
            "Disposable 一致性: 类型同时实现 IDisposable 和 IAsyncDisposable — 应二选一",
            "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable — 应二选一。项目统一模式：接口层用 IAsyncDisposable，消费方用 await using",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "A type implementing both IDisposable and IAsyncDisposable confuses consumers: use 'using' or 'await using'?" +
            "Project convention: 1) implement only IAsyncDisposable; 2) DisposeAsync() contains actual cleanup logic; 3) consumers use 'await using'." +
            "Exception: framework-mandated dual implementation (e.g. Stream subclasses) requires a comment explaining why.");

        private static readonly DiagnosticDescriptor RuleTrivialAsyncDispose = new(
            "JCC9103",
            "Disposable 一致性: DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
            "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable，但 DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "When DisposeAsync() merely calls Dispose() and returns ValueTask.CompletedTask, there is no real async cleanup logic." +
            "Correct approach: 1) remove the IDisposable interface declaration; 2) inline Dispose() logic into DisposeAsync(); 3) consumers switch to 'await using'." +
            "This eliminates dual-interface ambiguity and prevents misuse of synchronous Dispose.");

        private static readonly DiagnosticDescriptor RuleSyncUsingOnAsyncDisposable = new(
            "JCC9104",
            "Disposable 一致性: IAsyncDisposable 类型应使用 await using 而非 using",
            "类型 '{0}' 实现 IAsyncDisposable，应使用 'await using' 而非 'using'。using（同步）只调用 Dispose()，不会调用 DisposeAsync()，可能跳过异步清理逻辑。",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "When a type implements only IAsyncDisposable, 'using var' (synchronous) attempts to call Dispose(), but the type lacks Dispose(), causing the compiler to fall back to DisposeAsync().GetAwaiter().GetResult()," +
            "which may deadlock in synchronous contexts. Correct approach: always use 'await using var'.");

        private static readonly DiagnosticDescriptor RuleTryFinallyDispose = new(
            "JCC9105",
            "资源释放: try-finally 中手动 Dispose 应改用 using var / await using var",
            "try-finally 块中手动调用 '{0}' 释放资源，应改用 'using var'（同步）或 'await using var'（异步）声明，由编译器自动展开 try-finally。手动 try-finally 易遗漏、样板冗余、且无法保证异常安全。",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "AGENTS.md 规则1: 任何 IDisposable/IAsyncDisposable 对象在当前作用域内创建且不逃逸，必须用 'using var' / 'await using var' 声明。" +
            "正确做法: 1) 将变量声明改为 'using var x = new Xxx();' 或 'await using var x = new Xxx();'; 2) 删除手动 try-finally; 3) 编译器自动生成 try-finally 保证释放。" +
            "例外(不报告): a) try 块内有 return 语句(资源逃逸给调用方); b) 注释含 leave-open/escapes/factory/not-owning; c) 变量在 try 外声明且条件赋值(无法用 using var).");

        private static readonly DiagnosticDescriptor RuleDisposeTryCatch = new(
            "JCC9106",
            "资源释放: Dispose 方法内 try-catch 样板应改用 DisposeSafe 扩展方法",
            "Dispose/DisposeAsync 方法体内第 {0} 个 try-catch 捕获 {1}，是典型样板代码。应改用 DisposeSafe/DisposeSafeAsync/CancelAndDisposeSafe 扩展方法（JoinCode.Abstractions.Utils.DisposeSafeExtensions），消除 try-catch 样板。",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "AGENTS.md 规则3: Dispose 方法内禁止写 try { x.Dispose(); } catch (ObjectDisposedException) 样板，统一调 x.DisposeSafe(_logger)。" +
            "正确做法: 1) 'try { x.Dispose(); } catch (ObjectDisposedException) { ... }' → 'x.DisposeSafe(_logger)'; " +
            "2) 'try { await x.DisposeAsync(); } catch (ObjectDisposedException) { ... }' → 'await x.DisposeSafeAsync(_logger)'; " +
            "3) 'try { cts.Cancel(); } catch ...; try { cts.Dispose(); } catch ...' → 'cts.CancelAndDisposeSafe(_logger)'。" +
            "DisposeSafe 已吞 ObjectDisposedException（幂等），其他异常可选日志.");

        private static readonly DiagnosticDescriptor RuleSyncDisposeOnAsyncDisposable = new(
            "JCC9107",
            "资源释放: IAsyncDisposable 对象禁止同步 Dispose，必须用 DisposeAsync 收拢异步释放",
            "对 IAsyncDisposable 类型 '{0}' 调用同步 Dispose()，跳过了异步清理逻辑，导致资源泄露。必须收拢为 await DisposeAsync() 或 await using var 声明。",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "AGENTS.md 规则1: IAsyncDisposable 对象必须用异步释放。" +
            "正确做法: 1) 'x.Dispose()' → 'await x.DisposeAsync().ConfigureAwait(false)'; 2) 'using var x = ...' → 'await using var x = ...'; 3) try-finally 中 'x.Dispose()' → 'await x.DisposeAsync()'." +
            "原因: 同步 Dispose 不会调用 DisposeAsync，异步清理逻辑(如 flush buffer、close connection gracefully)被完全跳过，造成句柄泄露/数据丢失.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleDualDisposable, RuleTrivialAsyncDispose, RuleSyncUsingOnAsyncDisposable, RuleTryFinallyDispose, RuleDisposeTryCatch, RuleSyncDisposeOnAsyncDisposable);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
            context.RegisterSyntaxNodeAction(AnalyzeTryFinallyDispose, SyntaxKind.TryStatement);
            context.RegisterSyntaxNodeAction(AnalyzeDisposeMethodTryCatch, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeSyncDisposeOnAsyncDisposable, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var typeDecl = (TypeDeclarationSyntax)ctx.Node;
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            if (symbol is null) return;

            if (symbol.IsAbstract) return;

            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (idisposableType is null || iasyncDisposableType is null) return;

            var implementsIDisposable = symbol.Interfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
            var implementsIAsyncDisposable = symbol.Interfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);

            if (implementsIDisposable && implementsIAsyncDisposable)
            {
                var hasRealAsyncDispose = HasRealAsyncDispose(symbol);
                var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                ctx.ReportDiagnostic(Diagnostic.Create(
                    hasRealAsyncDispose ? RuleDualDisposable : RuleTrivialAsyncDispose,
                    typeDecl.Identifier.GetLocation(),
                    typeName));
            }
        }

        private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) return;
            if (localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) return;

            foreach (var variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer is null) continue;

                var typeInfo = ctx.SemanticModel.GetTypeInfo(variable.Initializer.Value, ctx.CancellationToken);
                var type = typeInfo.Type as INamedTypeSymbol;
                if (type is null) continue;

                var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
                if (iasyncDisposableType is null) return;

                var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
                if (idisposableType is null) return;

                var implementsIAsyncDisposable = type.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
                var implementsIDisposable = type.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default);

                if (implementsIAsyncDisposable && !implementsIDisposable)
                {
                    var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        RuleSyncUsingOnAsyncDisposable,
                        localDecl.UsingKeyword.GetLocation(),
                        typeName));
                }
            }
        }

        private static bool HasRealAsyncDispose(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers("DisposeAsync"))
            {
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

        private static void AnalyzeTryFinallyDispose(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var tryStmt = (TryStatementSyntax)ctx.Node;
            if (tryStmt.Finally is null) return;

            var finallyBlock = tryStmt.Finally.Block;
            if (finallyBlock is null) return;

            if (tryStmt.Catches.Count > 0) return;

            if (finallyBlock.DescendantNodes().Any(n =>
                n.IsKind(SyntaxKind.ForEachStatement) || n.IsKind(SyntaxKind.ForEachVariableStatement))) return;

            var allInvocations = finallyBlock.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            var disposeCalls = allInvocations.Where(IsDisposeCall).ToList();
            if (disposeCalls.Count == 0) return;

            if (allInvocations.Any(inv => !IsDisposeCall(inv))) return;

            if (ContainsReturnStatement(tryStmt.Block)) return;

            foreach (var call in disposeCalls)
            {
                if (!CanConvertToUsingVar(call, tryStmt, ctx.SemanticModel)) return;
            }

            var firstCall = disposeCalls[0];
            var callName = GetMemberName(firstCall);
            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleTryFinallyDispose,
                tryStmt.TryKeyword.GetLocation(),
                callName));
        }

        private static void AnalyzeDisposeMethodTryCatch(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var methodDecl = (MethodDeclarationSyntax)ctx.Node;
            var methodName = methodDecl.Identifier.ValueText;
            if (methodName is not "Dispose" and not "DisposeAsync") return;

            if (methodDecl.Body is null) return;

            var tryStatements = methodDecl.Body
                .DescendantNodes()
                .OfType<TryStatementSyntax>()
                .Where(t => t.Catches.Count > 0)
                .ToList();
            if (tryStatements.Count == 0) return;

            foreach (var tryStmt in tryStatements)
            {
                var catchInfo = AnalyzeCatchClauses(tryStmt.Catches);
                if (catchInfo is null) continue;

                if (!TryBlockIsSimpleDisposeOrCancel(tryStmt.Block)) continue;

                if (!IsSimpleCatchBlock(tryStmt.Catches)) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleDisposeTryCatch,
                    tryStmt.TryKeyword.GetLocation(),
                    catchInfo.Value.index,
                    catchInfo.Value.exceptionName));
            }
        }

        private static void AnalyzeSyncDisposeOnAsyncDisposable(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var invocation = (InvocationExpressionSyntax)ctx.Node;

            if (GetMemberName(invocation) is not "Dispose") return;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
            var receiver = memberAccess.Expression;

            var receiverType = ctx.SemanticModel.GetTypeInfo(receiver, ctx.CancellationToken).Type as INamedTypeSymbol;
            if (receiverType is null) return;

            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            if (iasyncDisposableType is null || idisposableType is null) return;

            var implementsIAsyncDisposable = receiverType.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default)
                || SymbolEqualityComparer.Default.Equals(receiverType, iasyncDisposableType);
            if (!implementsIAsyncDisposable) return;

            var implementsIDisposable = receiverType.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default)
                || SymbolEqualityComparer.Default.Equals(receiverType, idisposableType);
            if (implementsIDisposable) return;

            if (IsInsideDisposeMethod(invocation)) return;

            var typeName = receiverType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleSyncDisposeOnAsyncDisposable,
                invocation.GetLocation(),
                typeName));
        }

        private static bool IsInsideDisposeMethod(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax method)
                {
                    var name = method.Identifier.ValueText;
                    if (name is "Dispose" or "DisposeAsync")
                        return true;
                }
                current = current.Parent;
            }
            return false;
        }

        private static bool IsSimpleCatchBlock(SyntaxList<CatchClauseSyntax> catches)
        {
            foreach (var catchClause in catches)
            {
                if (catchClause.Block is null) continue;
                var statements = catchClause.Block.Statements;
                if (statements.Count == 0) continue;
                foreach (var stmt in statements)
                {
                    if (stmt is not ExpressionStatementSyntax exprStmt) return false;
                    if (exprStmt.Expression is not InvocationExpressionSyntax inv) return false;
                    var name = GetMemberName(inv);
                    if (name.StartsWith("Log", StringComparison.Ordinal) ||
                        name is "WriteLine" or "Write" or "WriteLineAsync" or "WriteAsync") continue;
                    return false;
                }
            }
            return true;
        }

        private static bool IsDisposeCall(InvocationExpressionSyntax invocation)
        {
            return GetMemberName(invocation) is "Dispose" or "DisposeAsync";
        }

        private static string GetMemberName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.ValueText;
            if (invocation.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText;
            return string.Empty;
        }

        private static bool ContainsReturnStatement(BlockSyntax? block)
        {
            if (block is null) return false;
            return block.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement) || n.IsKind(SyntaxKind.YieldReturnStatement));
        }

        private static bool CanConvertToUsingVar(
            InvocationExpressionSyntax disposeCall,
            TryStatementSyntax tryStmt,
            SemanticModel semanticModel)
        {
            if (disposeCall.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
            var receiver = memberAccess.Expression;

            if (receiver is not IdentifierNameSyntax identifier) return false;

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is null) return false;

            if (symbol is IFieldSymbol) return false;
            if (symbol is IParameterSymbol) return false;
            if (symbol is IPropertySymbol) return false;

            if (symbol is ILocalSymbol localSymbol)
            {
                var declRef = localSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (declRef is null) return false;
                var declNode = declRef.GetSyntax();
                if (declNode is not VariableDeclaratorSyntax varDecl) return false;

                if (varDecl.Initializer is null) return false;
                var initValue = varDecl.Initializer.Value;

                if (initValue.IsKind(SyntaxKind.NullLiteralExpression)) return false;
                if (initValue is DefaultExpressionSyntax) return false;

                if (!initValue.IsKind(SyntaxKind.ObjectCreationExpression) &&
                    !initValue.IsKind(SyntaxKind.ImplicitObjectCreationExpression)) return false;

                if (declNode.SpanStart >= tryStmt.Span.Start) return true;

                return true;
            }

            return false;
        }

        private static (int index, string exceptionName)? AnalyzeCatchClauses(SyntaxList<CatchClauseSyntax> catches)
        {
            for (var i = 0; i < catches.Count; i++)
            {
                var catchClause = catches[i];
                if (catchClause.Declaration is null) continue;
                var typeName = catchClause.Declaration.Type.ToString();
                if (typeName.Contains("ObjectDisposedException"))
                    return (i + 1, typeName);
                if (typeName.Contains("Exception"))
                    return (i + 1, typeName);
            }
            return null;
        }

        private static bool TryBlockIsSimpleDisposeOrCancel(BlockSyntax? block)
        {
            if (block is null) return false;
            if (block.Statements.Count == 0) return false;
            foreach (var stmt in block.Statements)
            {
                if (stmt is not ExpressionStatementSyntax exprStmt) return false;
                var expr = exprStmt.Expression;
                if (expr is AwaitExpressionSyntax awaitExpr)
                    expr = awaitExpr.Expression;
                if (expr is not InvocationExpressionSyntax inv) return false;
                var name = GetMemberName(inv);
                if (name is not "Dispose" and not "DisposeAsync" and not "Cancel" and not "CancelAsync")
                    return false;
            }
            return true;
        }
    }
}
