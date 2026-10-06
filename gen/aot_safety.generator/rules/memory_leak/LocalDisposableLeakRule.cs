namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9305: 局部 IDisposable/IAsyncDisposable 变量遗忘释放。
/// 在方法内创建或接管所有权(new X() / 调用返回 IDisposable 的方法)后,未用 using 声明、
/// 未调用释放方法(Dispose/DisposeAsync/DisposeSafe/DisposeSafeAsync/CancelAndDisposeSafe)、
/// 未转移所有权(return x / 赋值字段 / 作为参数传递)即视为泄露。
/// 典型案例: var tree = _parser.Parse(code); return tree?.RootNode; — tree 读取成员后未释放。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9305",
    Title = "内存泄漏: 局部IDisposable变量遗忘释放",
    Description = "局部变量 '{0}' 类型实现 IDisposable/IAsyncDisposable,未用 using 声明,且在方法内未释放(Dispose/DisposeAsync/DisposeSafe)或转移所有权(return/赋字段/传参)。将导致资源泄漏。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "局部IDisposable变量在方法内创建或接管所有权后,必须释放或转移所有权。正确做法: " +
        "1) 优先 using var x = new ...; / await using var x = ...; 自动释放; " +
        "2) 手动释放: x.Dispose(); / await x.DisposeAsync().ConfigureAwait(false); / x.DisposeSafe(_logger); " +
        "3) 转移所有权: return x; / _field = x; / TakeOwnership(x); (调用方接管); " +
        "4) 读取成员不算借用不算转移: return x?.RootNode; 时 x 仍需释放。" +
        "误报抑制: 若变量是借用(来自字段/参数读取而非新建),初始化器不是 new/调用,不会报告; 若确为借用且被误报,用 #pragma warning disable JCC9305 或 [SuppressMessage] 标注并注释说明借用来源。",
    IsCompilationEnd = false)]
public sealed class LocalDisposableLeakRule : AnalyzerRuleBase<LocalDisposableLeakRule> {
    private static readonly HashSet<string> ReleaseMethodNames = new(StringComparer.Ordinal) {
        "Dispose",
        "DisposeAsync",
        "DisposeSafe",
        "DisposeSafeAsync",
        "CancelAndDisposeSafe",
        "CancelAndDisposeSafeAsync",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeLocalDeclaration(ctx, idisposableType, iasyncDisposableType),
            SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(
        SyntaxNodeAnalysisContext ctx,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;
        if (idisposable is null && iasyncDisposable is null) return;

        var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) return;
        if (localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) return;
        if (localDecl.Declaration.Variables.Count != 1) return;

        var variable = localDecl.Declaration.Variables[0];
        if (variable.Initializer is null) return;

        var varSymbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as ILocalSymbol;
        if (varSymbol is null) return;
        if (varSymbol.IsConst) return;

        var varType = varSymbol.Type;
        if (varType is null || varType.SpecialType == SpecialType.System_Object) return;
        if (!IsDisposableType(varType, idisposable, iasyncDisposable)) return;
        if (IsExcludedResourceType(varType)) return;

        var init = variable.Initializer.Value;
        if (AotSafetyHelpers.IsNullLiteral(init)) return;
        if (!IsOwnershipAcquiringInit(init, ctx.SemanticModel, ctx.CancellationToken, idisposable, iasyncDisposable)) return;

        var methodBody = FindMethodBody(localDecl);
        if (methodBody is null) return;

        var (released, transferred) = ClassifyUsages(varSymbol, methodBody, ctx.SemanticModel, ctx.Compilation, ctx.CancellationToken);
        if (released || transferred) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, variable.Identifier.GetLocation(), variable.Identifier.ValueText));
    }

    /// <summary>
    /// 判断初始化器是否"获取所有权": new X() 或 调用返回 IDisposable 的方法(含 await 包裹)。
    /// 借用(字段读/已知容器 API 取元素/参数/局部读)不获取所有权,不报告。
    /// 方案 D「写法即语义」:只有 new 和非容器工厂返回 IDisposable 算拥有。
    /// </summary>
    private static bool IsOwnershipAcquiringInit(
        ExpressionSyntax init,
        SemanticModel semanticModel,
        CancellationToken ct,
        INamedTypeSymbol? idisposable,
        INamedTypeSymbol? iasyncDisposable) {
        var expr = UnwrapAwait(init);

        if (expr is ObjectCreationExpressionSyntax) return true;

        if (expr is ElementAccessExpressionSyntax) return false;

        if (expr is InvocationExpressionSyntax invocation) {
            if (IsContainerElementBorrowCall(invocation)) return false;
            if (IsKnownBorrowFactoryCall(invocation, semanticModel, ct)) return false;
            if (IsFieldReceiverBorrowCall(invocation)) return false;
            var returnType = semanticModel.GetTypeInfo(invocation, ct).Type;
            return returnType is not null && IsDisposableType(returnType, idisposable, iasyncDisposable);
        }

        return false;
    }

    private static ExpressionSyntax UnwrapAwait(ExpressionSyntax expr) {
        while (expr is AwaitExpressionSyntax awaitExpr) {
            expr = awaitExpr.Expression;
        }
        return expr;
    }

    /// <summary>
    /// 已知容器"取元素"方法名 — 调用返回的是容器内已有元素(借用,容器持有所有权)。
    /// 仅含通用 BCL/LINQ 模式,不含项目特定方法名(项目借用 API 按方案 D 改返回非 IDisposable 句柄)。
    /// </summary>
    private static readonly HashSet<string> ContainerElementAccessMethods = new(StringComparer.Ordinal) {
        "Get",
        "GetOrAdd",
        "GetOrAddAsync",
        "GetOrCreate",
        "GetOrCreateAsync",
        "First",
        "Single",
        "FirstOrDefault",
        "SingleOrDefault",
        "ElementAt",
        "ElementAtOrDefault",
        "GetValueOrDefault",
        "Peek",
        "Dequeue",
        "Pop",
    };

    /// <summary>
    /// 借用方法名前缀 — 以这些前缀开头的方法名视为借用(容器幂等获取模式)。
    /// GetOrCreate/GetOrAdd 是通用容器模式,变体名自动匹配,非项目特定知识。
    /// </summary>
    private static readonly string[] BorrowMethodPrefixes = ["GetOrCreate", "GetOrAdd"];

    /// <summary>
    /// 调用是否是已知容器取元素 API → 借用。支持 receiver.Method 和同类直接 Method 两种形式。
    /// 精确匹配 ContainerElementAccessMethods + 前缀匹配 BorrowMethodPrefixes。
    /// </summary>
    private static bool IsContainerElementBorrowCall(InvocationExpressionSyntax invocation) {
        var methodName = invocation.Expression switch {
            MemberAccessExpressionSyntax ma when ma.Expression is not null => ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            _ => null,
        };
        if (methodName is null) return false;
        if (ContainerElementAccessMethods.Contains(methodName)) return true;
        foreach (var prefix in BorrowMethodPrefixes) {
            if (methodName.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }
        // 同类隐式 this 调用的 Get* 方法 → 借用(从字段容器获取已有元素,方案 D 命名约定)。
        // MemberAccess 静态类型调用(Process.GetProcessById)不在此分支,不会误判新建。
        // 支持 IdentifierName(GetX()) 和 GenericName(GetService<T>())。
        if (invocation.Expression is (IdentifierNameSyntax or GenericNameSyntax) && methodName.StartsWith("Get", StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>
    /// 字段接收者的 Get* 方法调用 → 借用(从字段容器获取已有元素,容器持有所有权)。
    /// 方案 D「写法即语义」: receiver 是字段(_field / _field.Chain / this.field)且方法名以 Get 开头 → 借用。
    /// 不硬编码项目方法名,仅识别通用 Get 前缀 + 字段接收者模式。
    /// 静态类型调用(Process.GetProcessById)receiver 是类型名,IsFieldOrPropertyLeft 返回 false,不误判。
    /// </summary>
    private static bool IsFieldReceiverBorrowCall(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma) return false;
        var methodName = ma.Name.Identifier.ValueText;
        if (methodName.StartsWith("Get", StringComparison.Ordinal)) return IsFieldOrPropertyLeft(ma.Expression);
        if (methodName.StartsWith("Try", StringComparison.Ordinal)) return IsFieldOrPropertyLeft(ma.Expression);
        return false;
    }

    /// <summary>
    /// 已知 BCL"借用"调用 — 返回 IDisposable 但调用方不拥有(BCL 管理生命周期)。
    /// 仅识别 BCL 类型(Volatile/IHttpClientFactory/IServiceProvider),不识别项目自有类型(按方案 D 改返回类型)。
    /// - Volatile.Read(ref field) / Volatile.Write(ref field, value) — 字段读写,借用字段值
    /// - IHttpClientFactory.CreateClient(name) — BCL 管理客户端,GCAPI,调用方不 Dispose
    /// - IServiceProvider.GetService<T>() / GetRequiredService<T>() — DI 容器管理,调用方不 Dispose
    /// </summary>
    private static bool IsKnownBorrowFactoryCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (invocation.Expression is MemberAccessExpressionSyntax ma) {
            var methodName = ma.Name.Identifier.ValueText;
            if (methodName.Equals("Read", StringComparison.Ordinal) || methodName.Equals("Write", StringComparison.Ordinal)) {
                var receiverType = semanticModel.GetTypeInfo(ma.Expression, ct).Type;
                if (receiverType is not null && receiverType.Name.Equals("Volatile", StringComparison.Ordinal))
                    return true;
            }
            if (methodName.Equals("CreateClient", StringComparison.Ordinal)) {
                var receiverType = semanticModel.GetTypeInfo(ma.Expression, ct).Type;
                if (receiverType is not null && receiverType.Name.Equals("IHttpClientFactory", StringComparison.Ordinal))
                    return true;
            }
            if (methodName.Equals("GetService", StringComparison.Ordinal) || methodName.Equals("GetRequiredService", StringComparison.Ordinal)) {
                var receiverType = semanticModel.GetTypeInfo(ma.Expression, ct).Type;
                if (receiverType is not null && (receiverType.Name.Equals("IServiceProvider", StringComparison.Ordinal) || receiverType.Name.Equals("ServiceProvider", StringComparison.Ordinal)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 在方法体内分类对该局部变量的所有引用:是否释放、是否转移所有权。
    /// </summary>
    private static (bool Released, bool Transferred) ClassifyUsages(
        ILocalSymbol varSymbol,
        SyntaxNode methodBody,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct) {
        var released = false;
        var transferred = false;

        foreach (var identifier in methodBody.DescendantNodes().OfType<IdentifierNameSyntax>()) {
            if (ct.IsCancellationRequested) return (released, transferred);

            if (identifier.Identifier.ValueText != varSymbol.Name) continue;

            var symbol = semanticModel.GetSymbolInfo(identifier, ct).Symbol;
            if (!SymbolEqualityComparer.Default.Equals(symbol, varSymbol)) continue;

            if (identifier.Parent is EqualsValueClauseSyntax) continue;

            if (IsTransferToReturn(identifier)) {
                transferred = true;
                if (released || transferred) return (true, true);
                continue;
            }

            switch (identifier.Parent) {
                case MemberAccessExpressionSyntax ma when ReferenceEquals(ma.Expression, identifier):
                    if (IsReleaseCall(ma)) released = true;
                    else if (IsReleaseMethodRefTransferred(ma)) transferred = true;
                    else if (IsTransferToAwaitUsingViaConfigureAwait(ma)) transferred = true;
                    break;

                case ConditionalAccessExpressionSyntax ca when ReferenceEquals(ca.Expression, identifier):
                    if (IsReleaseConditionalAccess(ca)) released = true;
                    break;

                case UsingStatementSyntax usingStmt when ReferenceEquals(usingStmt.Expression, identifier):
                    released = true;
                    break;

                case AssignmentExpressionSyntax assign when ReferenceEquals(assign.Right, identifier):
                    if (IsFieldOrPropertyLeft(assign.Left) || IsWithInitializerProperty(assign)) transferred = true;
                    break;

                case ArgumentSyntax arg:
                    if (IsArgumentOwnershipTransfer(arg, semanticModel, compilation, ct)) transferred = true;
                    break;
            }

            if (released || transferred) return (true, true);
        }

        return (released, transferred);
    }

    /// <summary>
    /// 判定局部变量作为参数传递是否转移所有权。
    /// 调用 CrossMethodContractAnalyzer:转移方法(存字段/集合/return)→ 转移;借用方法(只读取)→ 不转移。
    /// 无法判定时保守转移(避免误报)。
    /// </summary>
    private static bool IsArgumentOwnershipTransfer(
        ArgumentSyntax arg,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct) {
        if (arg.Parent is not ArgumentListSyntax argList) return true;
        if (argList.Parent is not InvocationExpressionSyntax invocation) return true;

        var argIndex = argList.Arguments.IndexOf(arg);
        if (argIndex < 0) return true;

        return CrossMethodContractAnalyzer.IsTransferOwnership(invocation, argIndex, semanticModel, compilation, ct);
    }

    /// <summary>
    /// x.ReleaseMethod() 形式 — MemberAccess 父是 Invocation 且方法名是释放方法。
    /// </summary>
    private static bool IsReleaseCall(MemberAccessExpressionSyntax ma) {
        if (ma.Parent is not InvocationExpressionSyntax invocation) return false;
        if (!ReferenceEquals(invocation.Expression, ma)) return false;
        return ReleaseMethodNames.Contains(ma.Name.Identifier.ValueText);
    }

    /// <summary>
    /// x.Dispose 方法引用作为参数传递(创建委托转给容器)→ 释放责任转移给容器。
    /// 如 _undoChain.Add(new NonEmptyUndo(disposable.Dispose)) — 容器持有委托,卸载时执行。
    /// </summary>
    private static bool IsReleaseMethodRefTransferred(MemberAccessExpressionSyntax ma) {
        if (!ReleaseMethodNames.Contains(ma.Name.Identifier.ValueText)) return false;
        return ma.Parent is ArgumentSyntax;
    }

    /// <summary>
    /// x.ConfigureAwait(false) 作为 await using var y 的初始化器 → 所有权转移给 y。
    /// ConfiguredAsyncDisposable 结构体在 await using 释放时会回调原 IAsyncDisposable.DisposeAsync,
    /// 故原变量 x 的释放责任转移给 y,不再视为泄露。
    /// 典型: var fs = _fs.CreateStream(...); await using var cfg = fs.ConfigureAwait(false);
    /// </summary>
    private static bool IsTransferToAwaitUsingViaConfigureAwait(MemberAccessExpressionSyntax ma) {
        if (!ma.Name.Identifier.ValueText.Equals("ConfigureAwait", StringComparison.Ordinal)) return false;
        if (ma.Parent is not InvocationExpressionSyntax invocation) return false;
        if (invocation.Parent is not EqualsValueClauseSyntax equals) return false;
        if (equals.Parent is not VariableDeclaratorSyntax declarator) return false;
        if (declarator.Parent is not VariableDeclarationSyntax declaration) return false;
        if (declaration.Parent is not LocalDeclarationStatementSyntax localDecl) return false;
        return localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
            && localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword);
    }

    /// <summary>
    /// x?.ReleaseMethod() 形式 — ConditionalAccess 的 WhenNotNull 是释放调用。
    /// 注意: x?.Method() 中 WhenNotNull 的调用表达式是 MemberBindingExpressionSyntax(.Method),
    /// 不是 MemberAccessExpressionSyntax(x.Method)。
    /// </summary>
    private static bool IsReleaseConditionalAccess(ConditionalAccessExpressionSyntax ca) {
        if (ca.WhenNotNull is not InvocationExpressionSyntax invocation) return false;
        if (invocation.Expression is MemberBindingExpressionSyntax mb)
            return ReleaseMethodNames.Contains(mb.Name.Identifier.ValueText);
        return false;
    }

    /// <summary>
    /// 赋值左值是否字段/属性访问(this.field / _field / Property)。
    /// </summary>
    private static bool IsFieldOrPropertyLeft(ExpressionSyntax left) {
        if (left is ElementAccessExpressionSyntax ea)
            return IsFieldOrPropertyLeft(ea.Expression);
        if (left is MemberAccessExpressionSyntax) return true;
        if (left is IdentifierNameSyntax id) {
            var name = id.Identifier.ValueText.AsSpan();
            if (name.StartsWith("_".AsSpan(), StringComparison.Ordinal)) return true;
            return false;
        }
        return false;
    }

    /// <summary>
    /// 赋值是否在 record with 表达式或对象初始化器的 member initializer 中 → 属性赋值=转移所有权。
    /// 如: s with { BackgroundCts = backgroundCts } — backgroundCts 转移给新 record 的属性。
    /// 如: new TokenMonitor { Timer = timer } — timer 转移给新对象的属性。
    /// </summary>
    private static bool IsWithInitializerProperty(AssignmentExpressionSyntax assign) {
        if (assign.Parent is not InitializerExpressionSyntax init) return false;
        return init.Parent is WithExpressionSyntax or ObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// 判断标识符是否处于返回转移位置: return x / return x ?? fallback / return (x) / return x! 等。
    /// 跳过 CoalesceExpression 左操作数、括号、suppress-null 后缀,最终到达 ReturnStatement.Expression 即转移。
    /// </summary>
    private static bool IsTransferToReturn(SyntaxNode identifier) {
        var node = identifier;
        while (node.Parent is not null) {
            switch (node.Parent) {
                case ReturnStatementSyntax ret when ReferenceEquals(ret.Expression, node):
                    return true;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.CoalesceExpression) && ReferenceEquals(binary.Left, node):
                    node = binary;
                    break;

                case ParenthesizedExpressionSyntax paren when ReferenceEquals(paren.Expression, node):
                    node = paren;
                    break;

                case PostfixUnaryExpressionSyntax postfix when ReferenceEquals(postfix.Operand, node):
                    node = postfix;
                    break;

                default:
                    return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 找到包含该声明的最近方法体(SyntaxNode),用于引用分析。
    /// </summary>
    private static SyntaxNode? FindMethodBody(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax method) {
                return AotSafetyHelpers.GetMethodBody(method);
            }
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// 排除"非资源"的 IDisposable/IAsyncDisposable 类型 — Task/ValueTask 是异步操作句柄,
    /// await 即完成,非内存资源,不当泄露。即使 BCL 未来给 Task 加 IAsyncDisposable 也排除。
    /// </summary>
    private static bool IsExcludedResourceType(ITypeSymbol type) {
        var nameSpan = type.Name.AsSpan();
        if (nameSpan.StartsWith("Task".AsSpan(), StringComparison.Ordinal)) return true;
        if (nameSpan.StartsWith("ValueTask".AsSpan(), StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// 类型是否实现 IDisposable 或 IAsyncDisposable。
    /// </summary>
    private static bool IsDisposableType(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
        if (type is not INamedTypeSymbol namedType) return false;

        if (idisposable is not null && namedType.AllInterfaces.Contains(idisposable, SymbolEqualityComparer.Default))
            return true;
        if (iasyncDisposable is not null && namedType.AllInterfaces.Contains(iasyncDisposable, SymbolEqualityComparer.Default))
            return true;
        if (idisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, idisposable))
            return true;
        if (iasyncDisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, iasyncDisposable))
            return true;

        return false;
    }
}
