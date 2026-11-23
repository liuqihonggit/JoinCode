namespace AotSafety.Generator.Rules;

/// <summary>
/// 跨方法契约分析器:判定方法参数是转移(存字段/集合/return)还是借用(只读取)。
/// 方案 D「写法即语义」:无注解,分析方法体判定。
/// JCC9305 传参时调用本分析器,区分转移(不报)与借用(仍需释放)。
/// </summary>
internal static class CrossMethodContractAnalyzer {
    /// <summary>
    /// 判定 invocation 的指定参数位置是否转移所有权给被调用方法。
    /// 转移:被调用方法将参数存入字段/集合/return。
    /// 借用:被调用方法只读取参数(不存储)。
    /// 保守策略:BCL 方法默认借用;未知外部方法(无源码)保守转移(避免误报)。
    /// </summary>
    public static bool IsTransferOwnership(
        InvocationExpressionSyntax invocation,
        int argumentIndex,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken ct) {
        if (ct.IsCancellationRequested) return true;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
        if (methodSymbol is null) return true;
        if (argumentIndex < 0 || argumentIndex >= methodSymbol.Parameters.Length) return true;

        if (IsBclMethod(methodSymbol)) {
            return IsBclCollectionAddMethod(methodSymbol);
        }

        return AnalyzeMethodBodyTransfer(methodSymbol, argumentIndex, compilation, ct);
    }

    /// <summary>
    /// BCL 方法(非构造函数)默认借用 — 大多数 BCL 方法接收 IDisposable 参数只读取不存储。
    /// 构造函数(new StreamReader(stream)等)不在此处理,由 JCC9305 的其他逻辑判定。
    /// </summary>
    private static bool IsBclMethod(IMethodSymbol method) {
        var asm = method.ContainingAssembly;
        if (asm is null) return false;
        var asmName = asm.Name.AsSpan();
        return asmName.StartsWith("System".AsSpan(), StringComparison.Ordinal)
            || asmName.StartsWith("Microsoft".AsSpan(), StringComparison.Ordinal);
    }

    private static readonly HashSet<string> CollectionAddMethodNames = new(StringComparer.Ordinal) {
        "Add", "AddLast", "AddFirst", "Append", "Prepend",
        "Insert", "InsertAt", "InsertRange",
        "SetItem", "AddOrUpdate",
        "Push", "Enqueue",
    };

    private static readonly HashSet<string> BclTransferMethodNames = new(StringComparer.Ordinal) {
        "FromResult", "Exchange",
    };

    /// <summary>
    /// BCL 集合 Add/Insert/Push/Enqueue → 转移(参数存入集合)。
    /// Task.FromResult → 转移(参数包装返回)。
    /// Interlocked.Exchange → 转移(参数存入 ref 字段)。
    /// Options.Create → 转移(参数包装返回 IOptions)。
    /// 其他 BCL 方法 → 借用(只读取参数)。
    /// </summary>
    private static bool IsBclCollectionAddMethod(IMethodSymbol method) {
        if (CollectionAddMethodNames.Contains(method.Name)) return true;
        if (BclTransferMethodNames.Contains(method.Name)) return true;
        if (method.Name.Equals("Create", StringComparison.Ordinal)
            && method.ContainingType is not null
            && method.ContainingType.Name.Equals("Options", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// 分析方法体:指定参数是否转移所有权(存字段/集合/return)。
    /// 有源码 → 分析;无源码(外部方法)→ 保守转移(true)。
    /// </summary>
    private static bool AnalyzeMethodBodyTransfer(
        IMethodSymbol methodSymbol,
        int argumentIndex,
        Compilation compilation,
        CancellationToken ct) {
        var declRef = methodSymbol.DeclaringSyntaxReferences.Length > 0
            ? methodSymbol.DeclaringSyntaxReferences[0]
            : null;
        if (declRef is null) return true;

        var methodDecl = declRef.GetSyntax(ct) as MethodDeclarationSyntax;
        if (methodDecl is null) return true;

        var body = AotSafetyHelpers.GetMethodBody(methodDecl);
        if (body is null) return true;

        var model = compilation.GetSemanticModel(methodDecl.SyntaxTree);
        var paramSymbol = methodSymbol.Parameters[argumentIndex];

        return ParameterTransfersOwnership(paramSymbol, body, model, ct);
    }

    /// <summary>
    /// 检查参数符号在方法体内是否转移所有权:
    /// - 赋值给字段(_field = param / this.field = param)→ 转移
    /// - 插入集合(coll.Add(param) / dict[key] = param)→ 转移
    /// - 作为 return 返回(return param)→ 转移
    /// - 传给另一个方法(保守转移,避免递归复杂度)→ 转移
    /// - 只读取(调用方法/访问成员)→ 借用
    /// 有任一转移 → true;全部借用 → false。
    /// </summary>
    private static bool ParameterTransfersOwnership(
        IParameterSymbol paramSymbol,
        SyntaxNode body,
        SemanticModel model,
        CancellationToken ct) {
        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
            if (ct.IsCancellationRequested) return true;

            if (identifier.Identifier.ValueText != paramSymbol.Name) continue;

            var symbol = model.GetSymbolInfo(identifier, ct).Symbol;
            if (!SymbolEqualityComparer.Default.Equals(symbol, paramSymbol)) continue;

            if (identifier.Parent is EqualsValueClauseSyntax) continue;

            if (IsReturnTransfer(identifier)) return true;

            switch (identifier.Parent) {
                case MemberAccessExpressionSyntax ma when ReferenceEquals(ma.Expression, identifier):
                    if (IsReleaseCallOnParameter(ma)) return true;
                    break;

                case AssignmentExpressionSyntax assign when ReferenceEquals(assign.Right, identifier):
                    if (IsFieldOrPropertyLeft(assign.Left)) return true;
                    break;

                case ArgumentSyntax arg when IsCollectionAddArgument(arg, identifier, model, ct):
                    return true;

                case ArgumentSyntax:
                    return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> ReleaseMethodNames = new(StringComparer.Ordinal) {
        "Dispose",
        "DisposeAsync",
        "DisposeSafe",
        "DisposeSafeAsync",
        "CancelAndDisposeSafe",
        "CancelAndDisposeSafeAsync",
    };

    /// <summary>
    /// param.Dispose() / param.DisposeAsync() 等 — 释放责任转移给被调用方法。
    /// </summary>
    private static bool IsReleaseCallOnParameter(MemberAccessExpressionSyntax ma) {
        if (!ReleaseMethodNames.Contains(ma.Name.Identifier.ValueText)) return false;
        return ma.Parent is InvocationExpressionSyntax invocation && ReferenceEquals(invocation.Expression, ma);
    }

    /// <summary>
    /// 参数处于返回转移位置:return param / return (param) / return param! / 表达式体 => param 等。
    /// </summary>
    private static bool IsReturnTransfer(SyntaxNode identifier) {
        var node = identifier;
        while (node.Parent is not null) {
            switch (node.Parent) {
                case ReturnStatementSyntax ret when ReferenceEquals(ret.Expression, node):
                    return true;
                case ArrowExpressionClauseSyntax arrow when ReferenceEquals(arrow.Expression, node):
                    return true;
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
    /// 赋值左值是否字段/属性访问(this.field / _field / Property)。
    /// </summary>
    private static bool IsFieldOrPropertyLeft(ExpressionSyntax left) {
        if (left is ElementAccessExpressionSyntax ea)
            return IsFieldOrPropertyLeft(ea.Expression);
        if (left is MemberAccessExpressionSyntax) return true;
        if (left is IdentifierNameSyntax id) {
            var name = id.Identifier.ValueText.AsSpan();
            return name.StartsWith("_".AsSpan(), StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// 参数作为集合 Add/Insert/SetItem 等方法的参数传递 → 转移到集合。
    /// 如:coll.Add(param) / dict.Add(key, param) / list.Insert(0, param)。
    /// </summary>
    private static bool IsCollectionAddArgument(
        ArgumentSyntax arg,
        IdentifierNameSyntax identifier,
        SemanticModel model,
        CancellationToken ct) {
        if (arg.Parent is not ArgumentListSyntax argList) return false;
        if (argList.Parent is not InvocationExpressionSyntax invocation) return false;
        if (invocation.Expression is not MemberAccessExpressionSyntax ma) return false;

        var methodName = ma.Name.Identifier.ValueText;
        return methodName switch {
            "Add" or "AddLast" or "AddFirst" or "Append" or "Prepend" => true,
            "Insert" or "InsertAt" or "InsertRange" => true,
            "SetItem" or "AddOrUpdate" => true,
            "Push" or "Enqueue" => true,
            _ => false,
        };
    }
}
