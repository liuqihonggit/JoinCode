namespace AotSafety.Generator; 
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposableConsistencyRules : DiagnosticAnalyzer {
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

    private static readonly DiagnosticDescriptor RuleAsyncDisposableNotReleased = new(
        "JCC9108",
        "资源泄漏: IAsyncDisposable 局部变量创建后未释放",
        "局部变量 '{0}' 类型实现 IAsyncDisposable，但未用 'await using' 声明，方法内也未手动调用 DisposeAsync()。将导致异步资源泄漏(后台任务/Channel/专用线程不退出)，CI 偶发卡死。",
        "DisposableConsistency",
        DiagnosticSeverity.Warning,
        true,
        "IAsyncDisposable 对象必须释放,否则后台任务/Channel 消费者/专用线程永不退出,造成 CI 卡死。" +
        "正确做法: 1) 'var x = Create();' → 'await using var x = Create();'; " +
        "2) 若需手动释放: try { ... } finally { await x.DisposeAsync(); }; " +
        "3) 若所有权转移(返回/赋字段),添加注释 // not-owning 或 // escapes 标记豁免。" +
        "根因: Roslyn CA2000 不支持 IAsyncDisposable,此规则填补该盲区.");

    private static readonly DiagnosticDescriptor RuleAwaitInDispose = new(
        "JCC9200",
        "Dispose 释放完整性: Dispose/DisposeAsync 方法体内禁止 fire-and-forget 异步调用",
        "Dispose/DisposeAsync 方法体内 fire-and-forget 调用 '{0}'(第 {1} 行) — 释放路径射后不理会掩盖真实错误(异步清理未完成即返回)。必须改为 await 调用,确保释放完整完成。",
        "DisposableConsistency",
        DiagnosticSeverity.Error,
        true,
        "Root cause: fire-and-forget (_ = xxxAsync() or bare xxxAsync() without await) makes Dispose return before async cleanup completes, masking real errors." +
        "Fix: change '_ = xxxAsync()' to 'await xxxAsync().ConfigureAwait(false)'; in sync Dispose, remove the line (cannot await)." +
        "See CronSchedulerService.DisposeAsync and ActorBase.DisposeAsync for past deadlock incidents.");

    private static readonly DiagnosticDescriptor RuleDisposeGuard = new(
        "JCC9201",
        "Dispose 幂等守卫: Dispose() 方法体内必须有幂等守卫防止重复释放",
        "类型 '{0}' 实现 IDisposable 但 Dispose() 缺少幂等守卫(Interlocked.Exchange/CompareExchange 或 if(_disposed) return)。无守卫时重复调用 Dispose 会重复执行清理逻辑,导致 double-free/资源泄露/竞态条件。",
        "DisposableConsistency",
        DiagnosticSeverity.Error,
        true,
        "AGENTS.md: Dispose 必须幂等。正确模式: 1) 'if (Interlocked.Exchange(ref _disposed, 1) == 1) return;' 或 " +
        "2) 'if (_disposed) return; _disposed = true;' (非线程安全但也是守卫)。" +
        "例外: sealed 类 + 单次调用场景(如 using var)可豁免,但建议统一加守卫.");

    private static readonly DiagnosticDescriptor RuleDisposeAsyncGuard = new(
        "JCC9202",
        "Dispose 幂等守卫: DisposeAsync() 方法体内必须有幂等守卫防止重复释放",
        "类型 '{0}' 实现 IAsyncDisposable 但 DisposeAsync() 缺少幂等守卫(Interlocked.Exchange/CompareExchange 或 if(_disposed) return)。无守卫时重复调用 DisposeAsync 会重复执行清理逻辑,导致 double-free/资源泄露/竞态条件。",
        "DisposableConsistency",
        DiagnosticSeverity.Error,
        true,
        "AGENTS.md: DisposeAsync 必须幂等。正确模式: 1) 'if (Interlocked.Exchange(ref _disposed, 1) == 1) return;' 或 " +
        "2) 'if (_disposed) return; _disposed = true;' (非线程安全但也是守卫)。" +
        "例外: sealed 类 + 单次调用场景(如 await using var)可豁免,但建议统一加守卫.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleDualDisposable, RuleTrivialAsyncDispose, RuleSyncUsingOnAsyncDisposable, RuleTryFinallyDispose, RuleDisposeTryCatch, RuleSyncDisposeOnAsyncDisposable, RuleAsyncDisposableNotReleased, RuleAwaitInDispose, RuleDisposeGuard, RuleDisposeAsyncGuard);

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterSyntaxNodeAction(AnalyzeTryFinallyDispose, SyntaxKind.TryStatement);
        context.RegisterSyntaxNodeAction(AnalyzeDisposeMethodTryCatch, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeSyncDisposeOnAsyncDisposable, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDisposeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDisposeGuard, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx) {
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

        if (implementsIDisposable && implementsIAsyncDisposable) {
            var hasRealAsyncDispose = HasRealAsyncDispose(symbol);
            var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            ctx.ReportDiagnostic(Diagnostic.Create(
                hasRealAsyncDispose ? RuleDualDisposable : RuleTrivialAsyncDispose,
                typeDecl.Identifier.GetLocation(),
                typeName));
        }
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

        var hasUsing = localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword);
        var hasAwait = localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword);

        if (hasUsing && !hasAwait) {
            AnalyzeSyncUsingOnAsyncDisposable(ctx, localDecl);
        }

        if (!hasUsing) {
            AnalyzeAsyncDisposableNotReleased(ctx, localDecl);
        }
    }

    private static void AnalyzeSyncUsingOnAsyncDisposable(SyntaxNodeAnalysisContext ctx, LocalDeclarationStatementSyntax localDecl) {
        foreach (var variable in localDecl.Declaration.Variables) {
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

            if (implementsIAsyncDisposable && !implementsIDisposable) {
                var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleSyncUsingOnAsyncDisposable,
                    localDecl.UsingKeyword.GetLocation(),
                    typeName));
            }
        }
    }

    /// <summary>
    /// JCC9108: 检测 IAsyncDisposable 局部变量创建后未释放(无 await using,无手动 DisposeAsync,无所有权转移)。
    /// 填补 Roslyn CA2000 不支持 IAsyncDisposable 的盲区。
    /// </summary>
    private static void AnalyzeAsyncDisposableNotReleased(SyntaxNodeAnalysisContext ctx, LocalDeclarationStatementSyntax localDecl) {
        var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        if (iasyncDisposableType is null) return;

        var containingMethod = localDecl.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod is null) return;

        if (containingMethod.Identifier.ValueText == "Main" &&
            containingMethod.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;

        foreach (var variable in localDecl.Declaration.Variables) {
            if (variable.Initializer is null) continue;
            if (variable.Initializer.Value.IsKind(SyntaxKind.NullLiteralExpression)) continue;

            if (IsFetchedNotCreated(variable.Initializer.Value)) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(variable.Initializer.Value, ctx.CancellationToken);
            var type = typeInfo.Type as INamedTypeSymbol;
            if (type is null) continue;

            var implementsIAsyncDisposable = type.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            if (!implementsIAsyncDisposable) continue;

            var varName = variable.Identifier.ValueText;

            if (HasManualDisposeAsync(containingMethod, varName)) continue;
            if (IsOwnershipTransferred(containingMethod, varName, ctx.SemanticModel, ctx.CancellationToken)) continue;
            if (IsCapturedByAwaitUsing(containingMethod, varName)) continue;
            if (IsCapturedByUsingStatement(containingMethod, varName)) continue;
            if (IsCapturedByUsingVar(containingMethod, varName)) continue;
            if (HasExemptionComment(localDecl)) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleAsyncDisposableNotReleased,
                variable.Identifier.GetLocation(),
                varName));
        }
    }

    /// <summary>
    /// 判断初始化表达式是否为"获取而非新建"——顶层不是 ObjectCreationExpression 即从字段/属性/方法获取,不拥有所有权。
    /// 只检查顶层,不递归检查方法调用参数中的 new(如 Factory.Create(new Options()) 顶层是方法调用,应跳过)。
    /// 纯语法结构分析,不依赖方法名前缀,可共享给其他项目。
    /// </summary>
    private static bool IsFetchedNotCreated(SyntaxNode initValue) {
        if (initValue is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) return false;
        if (initValue is AwaitExpressionSyntax awaitExpr) return IsFetchedNotCreated(awaitExpr.Expression);
        if (initValue is ParenthesizedExpressionSyntax paren) return IsFetchedNotCreated(paren.Expression);
        return true;
    }

    /// <summary>
    /// 检查方法体内是否手动调用了 varName.DisposeAsync()。
    /// </summary>
    private static bool HasManualDisposeAsync(SyntaxNode method, string varName) {
        foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.ValueText == "DisposeAsync" &&
                ma.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == varName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查变量所有权是否转移(纯语法结构分析,不依赖方法名前缀):
    /// return x / this.field = x / dict[key] = x / new Wrapper(x) 后 return 或赋值给字段/索引器 / 对象初始化器属性赋值 / lambda 引用
    /// </summary>
    private static bool IsOwnershipTransferred(SyntaxNode method, string varName, SemanticModel? semanticModel = null, CancellationToken ct = default) {
        foreach (var ret in method.DescendantNodes().OfType<ReturnStatementSyntax>()) {
            if (ret.Expression is not null && ContainsIdentifier(ret.Expression, varName))
                return true;
        }

        foreach (var assign in method.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (!ContainsIdentifier(assign.Right, varName)) continue;
            if (assign.Left is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax) return true;
            if (assign.Left is IdentifierNameSyntax leftId && semanticModel is not null) {
                if (semanticModel.GetSymbolInfo(leftId, ct).Symbol is IFieldSymbol) return true;
            }
        }

        foreach (var objCreate in method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
            if (objCreate.ArgumentList is null) continue;
            if (!objCreate.ArgumentList.Arguments.Any(arg => ContainsIdentifier(arg.Expression, varName))) continue;
            var parent = objCreate.Parent;
            while (parent is not null) {
                if (parent is ReturnStatementSyntax) return true;
                if (parent is AssignmentExpressionSyntax assign &&
                    assign.Left is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax) return true;
                parent = parent.Parent;
            }
        }

        foreach (var objInit in method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
            if (objInit.Initializer is null) continue;
            foreach (var assign in objInit.Initializer.Expressions.OfType<AssignmentExpressionSyntax>()) {
                if (ContainsIdentifier(assign.Right, varName)) {
                    var parent = objInit.Parent;
                    while (parent is not null) {
                        if (parent is ReturnStatementSyntax) return true;
                        if (parent is AssignmentExpressionSyntax outerAssign &&
                            outerAssign.Left is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax) return true;
                        parent = parent.Parent;
                    }
                }
            }
        }

        if (IsReferencedInLambda(method, varName)) return true;

        return false;
    }

    /// <summary>
    /// 检查变量是否被 await using 通过 ConfigureAwait 模式接管:
    /// `await using var y = x.ConfigureAwait(false)` — x 被 y 的 await using 释放。
    /// </summary>
    private static bool IsCapturedByAwaitUsing(SyntaxNode method, string varName) {
        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()) {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;
            if (!localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) continue;

            foreach (var var in localDecl.Declaration.Variables) {
                if (var.Initializer?.Value is not InvocationExpressionSyntax inv) continue;
                if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
                if (ma.Name.Identifier.ValueText != "ConfigureAwait") continue;
                if (ma.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 检查变量是否被 using var 间接接管: `using var y = SomeMethod(x)` — x 被 y 的 using 释放。
    /// </summary>
    private static bool IsCapturedByUsingVar(SyntaxNode method, string varName) {
        foreach (var localDecl in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()) {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;

            foreach (var var in localDecl.Declaration.Variables) {
                if (var.Initializer is null) continue;
                if (ContainsIdentifier(var.Initializer.Value, varName))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 检查变量是否被 await using 块语句接管: `await using (x) { ... }` 或 `await using (x.ConfigureAwait(false)) { ... }`。
    /// </summary>
    private static bool IsCapturedByUsingStatement(SyntaxNode method, string varName) {
        foreach (var usingStmt in method.DescendantNodes().OfType<UsingStatementSyntax>()) {
            if (usingStmt.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == varName)
                return true;
            if (usingStmt.Expression is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Expression is IdentifierNameSyntax id2 && id2.Identifier.ValueText == varName)
                return true;
        }
        return false;
    }

    private static bool IsReferencedInLambda(SyntaxNode method, string varName) {
        foreach (var lambda in method.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>()) {
            if (ContainsIdentifier(lambda, varName)) return true;
        }
        foreach (var lambda in method.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>()) {
            if (ContainsIdentifier(lambda, varName)) return true;
        }
        foreach (var localFunc in method.DescendantNodes().OfType<LocalFunctionStatementSyntax>()) {
            if (localFunc.Body is not null && ContainsIdentifier(localFunc.Body, varName)) return true;
        }
        return false;
    }

    private static bool ContainsIdentifier(SyntaxNode node, string varName) {
        foreach (var id in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
            if (id.Identifier.ValueText == varName) return true;
        }
        return false;
    }

    private static bool IsIdentifier(SyntaxNode node, string varName) {
        return node is IdentifierNameSyntax id && id.Identifier.ValueText == varName;
    }

    /// <summary>
    /// 检查声明是否有豁免注释(not-owning/escapes/factory/leave-open)。
    /// </summary>
    private static bool HasExemptionComment(LocalDeclarationStatementSyntax localDecl) {
        var trivia = localDecl.GetLeadingTrivia().ToString() + localDecl.GetTrailingTrivia().ToString();
        return trivia.Contains("not-owning") || trivia.Contains("escapes") ||
               trivia.Contains("factory") || trivia.Contains("leave-open");
    }

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

    private static void AnalyzeTryFinallyDispose(SyntaxNodeAnalysisContext ctx) {
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

        foreach (var call in disposeCalls) {
            if (!CanConvertToUsingVar(call, tryStmt, ctx.SemanticModel)) return;
        }

        var firstCall = disposeCalls[0];
        var callName = GetMemberName(firstCall);
        ctx.ReportDiagnostic(Diagnostic.Create(
            RuleTryFinallyDispose,
            tryStmt.TryKeyword.GetLocation(),
            callName));
    }

    private static void AnalyzeDisposeMethodTryCatch(SyntaxNodeAnalysisContext ctx) {
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

        foreach (var tryStmt in tryStatements) {
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

    private static void AnalyzeSyncDisposeOnAsyncDisposable(SyntaxNodeAnalysisContext ctx) {
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

    private static bool IsInsideDisposeMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax method) {
                var name = method.Identifier.ValueText;
                if (name is "Dispose" or "DisposeAsync")
                    return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static bool IsSimpleCatchBlock(SyntaxList<CatchClauseSyntax> catches) {
        foreach (var catchClause in catches) {
            if (catchClause.Block is null) continue;
            var statements = catchClause.Block.Statements;
            if (statements.Count == 0) continue;
            foreach (var stmt in statements) {
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

    private static bool IsDisposeCall(InvocationExpressionSyntax invocation) {
        return GetMemberName(invocation) is "Dispose" or "DisposeAsync";
    }

    private static string GetMemberName(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        if (invocation.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText;
        return string.Empty;
    }

    private static bool ContainsReturnStatement(BlockSyntax? block) {
        if (block is null) return false;
        return block.DescendantNodes().Any(n => n.IsKind(SyntaxKind.ReturnStatement) || n.IsKind(SyntaxKind.YieldReturnStatement));
    }

    private static bool CanConvertToUsingVar(
        InvocationExpressionSyntax disposeCall,
        TryStatementSyntax tryStmt,
        SemanticModel semanticModel) {
        if (disposeCall.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        var receiver = memberAccess.Expression;

        if (receiver is not IdentifierNameSyntax identifier) return false;

        var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
        if (symbol is null) return false;

        if (symbol is IFieldSymbol) return false;
        if (symbol is IParameterSymbol) return false;
        if (symbol is IPropertySymbol) return false;

        if (symbol is ILocalSymbol localSymbol) {
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

    private static (int index, string exceptionName)? AnalyzeCatchClauses(SyntaxList<CatchClauseSyntax> catches) {
        for (var i = 0; i < catches.Count; i++) {
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

    private static bool TryBlockIsSimpleDisposeOrCancel(BlockSyntax? block) {
        if (block is null) return false;
        if (block.Statements.Count == 0) return false;
        foreach (var stmt in block.Statements) {
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

    private static void AnalyzeDisposeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodName = methodDecl.Identifier.ValueText;
        if (methodName is not ("Dispose" or "DisposeAsync")) return;

        var body = methodDecl.Body;
        if (body is null) return;

        foreach (var stmt in body.DescendantNodes().OfType<ExpressionStatementSyntax>()) {
            if (IsInsideLambdaOrLocalFunction(stmt, body)) continue;

            var expr = stmt.Expression;
            string? fireAndForgetMethodName = null;

            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax { Identifier.ValueText: "_" }) {
                var invoked = assign.Right;
                if (invoked is InvocationExpressionSyntax inv)
                    fireAndForgetMethodName = GetInvocationName(inv);
            } else if (expr is InvocationExpressionSyntax inv) {
                fireAndForgetMethodName = GetInvocationName(inv);
            }

            if (fireAndForgetMethodName is null) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(expr);
            if (IsTaskType(typeInfo.Type)) {
                var line = stmt.SyntaxTree.GetLineSpan(stmt.Span).StartLinePosition.Line + 1;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    RuleAwaitInDispose,
                    stmt.Expression.GetLocation(),
                    fireAndForgetMethodName,
                    line));
            }
        }
    }

    private static string? GetInvocationName(InvocationExpressionSyntax inv) {
        return inv.Expression switch {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsTaskType(ITypeSymbol? type) {
        if (type is null) return false;
        var name = type.OriginalDefinition.ToDisplayString();
        return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.Task<T>" or "System.Threading.Tasks.ValueTask<T>";
    }

    private static void AnalyzeDisposeGuard(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodName = methodDecl.Identifier.ValueText;

        var isSyncDispose = methodName == "Dispose" && methodDecl.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        var isAsyncDispose = methodName == "DisposeAsync";
        if (!isSyncDispose && !isAsyncDispose) return;

        var body = methodDecl.Body;
        if (body is null) return;

        var typeDecl = methodDecl.Parent as TypeDeclarationSyntax;
        if (typeDecl is null) return;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (symbol is null) return;
        if (symbol.IsAbstract) return;

        if (!HasInstanceFields(symbol)) return;

        if (isSyncDispose) {
            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            if (idisposableType is null) return;
            var implementsIDisposable = symbol.Interfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
            if (!implementsIDisposable) return;
        } else {
            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (iasyncDisposableType is null) return;
            var implementsIAsyncDisposable = symbol.Interfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            if (!implementsIAsyncDisposable) return;
        }

        if (HasDisposeGuard(body)) return;
        if (HasDelegateGuard(body, ctx.SemanticModel, symbol)) return;

        var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        ctx.ReportDiagnostic(Diagnostic.Create(
            isSyncDispose ? RuleDisposeGuard : RuleDisposeAsyncGuard,
            methodDecl.Identifier.GetLocation(),
            typeName));
    }

    private static bool HasInstanceFields(INamedTypeSymbol type) {
        foreach (var member in type.GetMembers()) {
            if (member is IFieldSymbol { IsStatic: false } field) {
                if (field.IsConst) continue;
                if (field.IsImplicitlyDeclared) continue;
                return true;
            }
        }
        return false;
    }

    private static bool HasDisposeGuard(BlockSyntax body) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (invocation.Expression is MemberAccessExpressionSyntax ma) {
                var methodName = ma.Name.Identifier.ValueText;
                if (methodName is "Exchange" or "CompareExchange") {
                    var receiverName = ma.Expression switch {
                        IdentifierNameSyntax id => id.Identifier.ValueText,
                        MemberAccessExpressionSyntax inner => inner.Name.Identifier.ValueText,
                        _ => null
                    };
                    if (receiverName is "Interlocked" or "Volatile") return true;
                }
            }
        }

        foreach (var ifStmt in body.DescendantNodes().OfType<IfStatementSyntax>()) {
            var hasReturn = ifStmt.Statement is ReturnStatementSyntax ||
                (ifStmt.Statement is BlockSyntax b && b.Statements.Count == 1 && b.Statements[0] is ReturnStatementSyntax);
            if (hasReturn) return true;
        }

        return false;
    }

    private static bool HasDelegateGuard(BlockSyntax body, SemanticModel semanticModel, INamedTypeSymbol currentType) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (IsInsideLambdaOrLocalFunction(invocation, body)) continue;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod) continue;

            if (!SymbolEqualityComparer.Default.Equals(calledMethod.ContainingType, currentType)) continue;

            foreach (var decl in calledMethod.DeclaringSyntaxReferences) {
                if (decl.GetSyntax() is MethodDeclarationSyntax { Body: { } calleeBody }) {
                    if (HasDisposeGuard(calleeBody)) return true;
                }
            }
        }
        return false;
    }

    private static bool IsInsideLambdaOrLocalFunction(SyntaxNode node, BlockSyntax methodBody) {
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
}