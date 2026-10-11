namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9308: 事件订阅未取消泄露。
/// 状态机驱动:对每个事件订阅点跟踪 Subscribed → Cancelled/Leaked。
/// 检测:字段/属性/参数的事件订阅 (receiver.Event += handler) 在 Dispose/DisposeAsync 中无对应 (-=)。
/// lambda 订阅无法取消(无委托引用),直接报告。
/// 不检测:自身事件(Event += handler,对象回收时自动清理)、局部变量事件(生命周期短)。
/// 排除:事件访问器(add/remove)内的 += 是事件转发,不是订阅。
/// 支持:表达式体 Dispose(=> ...)中的 -= 取消。
/// 增强:追踪 Dispose 调用链(辅助方法中的 -=),参数→字段符号匹配(构造函数 _field = param)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9308",
    Title = "内存泄漏: 事件订阅未取消",
    Description = "接收者 '{0}' 的事件 '{1}' 订阅了但在 Dispose/DisposeAsync 中未取消订阅,将导致订阅方无法被 GC 回收。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri =
        "事件订阅必须在对象 Dispose 时取消,否则事件源持有订阅方引用导致无法 GC。" +
        "正确做法: " +
        "1) 方法处理器: 在 Dispose/DisposeAsync 中对应 receiver.Event -= HandlerMethod; " +
        "2) lambda 处理器: 改为有名方法(可 -=),或用 EventHandler 变量持有委托引用后 -=; " +
        "3) 构造函数参数事件: 将参数存为字段,在 Dispose 中用字段取消。" +
        "误报抑制: 若事件源生命周期与订阅方相同(同生共死),用 #pragma warning disable JCC9308 或 [SuppressMessage] 标注并注释说明。",
    IsCompilationEnd = false)]
public sealed class EventSubscriptionLeakRule : AnalyzerRuleBase<EventSubscriptionLeakRule> {

    /// <summary>事件订阅生命周期状态机</summary>
    private enum SubscriptionLifecycle {
        /// <summary>已订阅,等待匹配取消</summary>
        Subscribed,
        /// <summary>已取消:Dispose 中找到对应 -=</summary>
        Cancelled,
        /// <summary>泄露:Dispose 完成仍未取消,或 lambda 无法取消</summary>
        Leaked,
    }

    private static readonly HashSet<string> DisposeMethodNames = new(StringComparer.Ordinal) {
        "Dispose", "DisposeAsync", "DisposeCore", "DisposeAsyncCore",
        "DisposeAsyncInternal", "DisposeInternal", "PostStopAsync",
        "StopAsync", "Close", "ShutdownAsync", "CleanupAsync", "Detach",
        "DisconnectAsync",
    };

    /// <summary>订阅点:receiver.Event += handler</summary>
    private sealed class SubscriptionPoint(
        ISymbol? receiverSymbol,
        string eventName,
        string? handlerName,
        bool isLambda,
        string receiverDisplay,
        Location location) {
        public ISymbol? ReceiverSymbol { get; } = receiverSymbol;
        public string EventName { get; } = eventName;
        public string? HandlerName { get; } = handlerName;
        public bool IsLambda { get; } = isLambda;
        public string ReceiverDisplay { get; } = receiverDisplay;
        public Location Location { get; } = location;
    }

    /// <summary>取消点:receiver.Event -= handler</summary>
    private sealed class CancellationPoint(
        ISymbol? receiverSymbol,
        string eventName,
        string? handlerName) {
        public ISymbol? ReceiverSymbol { get; } = receiverSymbol;
        public string EventName { get; } = eventName;
        public string? HandlerName { get; } = handlerName;
    }

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeDeclaration(ctx),
            SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        var semanticModel = ctx.SemanticModel;
        var ct = ctx.CancellationToken;

        var disposeBodies = CollectDisposeBodies(typeDecl);
        if (disposeBodies.Count == 0) return;

        var allMethods = CollectAllMethodBodies(typeDecl, semanticModel, ct);
        var paramToField = CollectParamToFieldMappings(typeDecl, semanticModel, ct);

        var subscriptions = CollectSubscriptions(typeDecl, disposeBodies, semanticModel, ct);
        if (subscriptions.Count == 0) return;

        var cancellations = CollectCancellations(disposeBodies, allMethods, semanticModel, ct);

        foreach (var sub in subscriptions) {
            var lifecycle = ClassifySubscription(sub, cancellations, paramToField);
            if (lifecycle == SubscriptionLifecycle.Leaked)
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, sub.Location, sub.ReceiverDisplay, sub.EventName));
        }
    }

    /// <summary>状态机:Subscribed → Cancelled(匹配 -=)/ Leaked(未匹配或 lambda)</summary>
    private static SubscriptionLifecycle ClassifySubscription(
        SubscriptionPoint sub,
        List<CancellationPoint> cancellations,
        Dictionary<ISymbol, ISymbol> paramToField) {
        if (sub.IsLambda) return SubscriptionLifecycle.Leaked;
        return IsCancelled(sub, cancellations, paramToField)
            ? SubscriptionLifecycle.Cancelled
            : SubscriptionLifecycle.Leaked;
    }

    private static bool IsCancelled(
        SubscriptionPoint sub,
        List<CancellationPoint> cancellations,
        Dictionary<ISymbol, ISymbol> paramToField) {
        foreach (var cancel in cancellations) {
            if (!string.Equals(cancel.EventName, sub.EventName, StringComparison.Ordinal)) continue;
            if (!string.Equals(cancel.HandlerName, sub.HandlerName, StringComparison.Ordinal)) continue;
            if (SymbolEqualityComparer.Default.Equals(cancel.ReceiverSymbol, sub.ReceiverSymbol)) return true;
            if (sub.ReceiverSymbol is not null && cancel.ReceiverSymbol is not null
                && sub.ReceiverSymbol.Name == cancel.ReceiverSymbol.Name
                && sub.ReceiverSymbol.Kind == cancel.ReceiverSymbol.Kind) return true;
            if (sub.ReceiverSymbol is not null && paramToField.TryGetValue(sub.ReceiverSymbol, out var mappedField)
                && SymbolEqualityComparer.Default.Equals(cancel.ReceiverSymbol, mappedField)) return true;
            if (cancel.ReceiverSymbol is not null && paramToField.TryGetValue(cancel.ReceiverSymbol, out var mappedField2)
                && SymbolEqualityComparer.Default.Equals(sub.ReceiverSymbol, mappedField2)) return true;
        }
        return false;
    }

    /// <summary>收集 Dispose 方法体(支持 BlockSyntax 和表达式体 ArrowExpressionClause)</summary>
    private static List<SyntaxNode> CollectDisposeBodies(TypeDeclarationSyntax typeDecl) {
        var result = new List<SyntaxNode>();
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>()) {
            if (!DisposeMethodNames.Contains(method.Identifier.ValueText)) continue;
            if (method.Body is not null)
                result.Add(method.Body);
            else if (method.ExpressionBody is not null)
                result.Add(method.ExpressionBody);
        }
        return result;
    }

    /// <summary>收集类型中所有方法体(用于调用链追踪)</summary>
    private static Dictionary<IMethodSymbol, SyntaxNode> CollectAllMethodBodies(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var result = new Dictionary<IMethodSymbol, SyntaxNode>(SymbolEqualityComparer.Default);
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>()) {
            var methodSymbol = semanticModel.GetDeclaredSymbol(method, ct) as IMethodSymbol;
            if (methodSymbol is null) continue;
            if (method.Body is not null)
                result[methodSymbol] = method.Body;
            else if (method.ExpressionBody is not null)
                result[methodSymbol] = method.ExpressionBody;
        }
        return result;
    }

    /// <summary>收集构造函数中 _field = param 赋值,建立参数→字段映射</summary>
    private static Dictionary<ISymbol, ISymbol> CollectParamToFieldMappings(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var result = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>()) {
            if (ctor.Body is null) continue;
            foreach (var assign in ctor.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
                if (ct.IsCancellationRequested) return result;
                var leftSymbol = semanticModel.GetSymbolInfo(assign.Left, ct).Symbol;
                var rightSymbol = semanticModel.GetSymbolInfo(assign.Right, ct).Symbol;
                if (leftSymbol is IFieldSymbol && rightSymbol is IParameterSymbol)
                    result[rightSymbol] = leftSymbol;
            }
        }
        return result;
    }

    private static List<SubscriptionPoint> CollectSubscriptions(
        TypeDeclarationSyntax typeDecl,
        List<SyntaxNode> disposeBodies,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var result = new List<SubscriptionPoint>();
        var disposeBodySet = new HashSet<SyntaxNode>(disposeBodies);

        foreach (var assign in typeDecl.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return result;
            if (!assign.IsKind(SyntaxKind.AddAssignmentExpression)) continue;
            if (IsInNestedType(assign, typeDecl)) continue;
            if (IsInDisposeBody(assign, disposeBodySet)) continue;
            if (IsInEventAccessor(assign)) continue;
            if (IsCancelledInSameMethod(assign, semanticModel, ct)) continue;
            if (IsCancelledInAnyMethod(assign, typeDecl, semanticModel, ct)) continue;

            var (receiverSymbol, eventName, receiverDisplay) = ExtractSubscriptionReceiver(assign.Left, semanticModel, ct);
            if (eventName is null) continue;
            if (receiverSymbol is not (IFieldSymbol or IPropertySymbol or IParameterSymbol)) continue;

            var (handlerName, isLambda) = ClassifyHandler(assign.Right);
            result.Add(new SubscriptionPoint(receiverSymbol, eventName, handlerName, isLambda, receiverDisplay, assign.GetLocation()));
        }
        return result;
    }

    /// <summary>从订阅左边提取接收者符号、事件名、显示名(支持 MemberAccess 和 ConditionalAccess ?.,排除 base;只接受 IEventSymbol)</summary>
    private static (ISymbol? ReceiverSymbol, string? EventName, string ReceiverDisplay) ExtractSubscriptionReceiver(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (left is MemberAccessExpressionSyntax ma) {
            if (ma.Expression.IsKind(SyntaxKind.BaseExpression)) return (null, null, "");
            if (semanticModel.GetSymbolInfo(ma, ct).Symbol is not IEventSymbol) return (null, null, "");
            return (semanticModel.GetSymbolInfo(ma.Expression, ct).Symbol, ma.Name.Identifier.ValueText, ma.Expression.ToString());
        }
        if (left is ConditionalAccessExpressionSyntax ca && ca.WhenNotNull is MemberBindingExpressionSyntax mb) {
            if (ca.Expression.IsKind(SyntaxKind.BaseExpression)) return (null, null, "");
            if (semanticModel.GetSymbolInfo(ca, ct).Symbol is not IEventSymbol) return (null, null, "");
            return (semanticModel.GetSymbolInfo(ca.Expression, ct).Symbol, mb.Name.Identifier.ValueText, ca.Expression.ToString());
        }
        return (null, null, "");
    }

    /// <summary>收集取消点:Dispose 方法体 + 追踪调用链(辅助方法中的 -=)</summary>
    private static List<CancellationPoint> CollectCancellations(
        List<SyntaxNode> disposeBodies,
        Dictionary<IMethodSymbol, SyntaxNode> allMethods,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var result = new List<CancellationPoint>();
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var body in disposeBodies) {
            CollectCancellationsFromBody(body, result, semanticModel, ct);
            TraceCallChainForCancellations(body, result, allMethods, semanticModel, ct, visited);
        }
        return result;
    }

    private static void CollectCancellationsFromBody(
        SyntaxNode body,
        List<CancellationPoint> result,
        SemanticModel semanticModel,
        CancellationToken ct) {
        foreach (var assign in body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return;
            if (!assign.IsKind(SyntaxKind.SubtractAssignmentExpression)) continue;

            var (receiverSymbol, eventName) = ExtractReceiverAndEvent(assign.Left, semanticModel, ct);
            if (eventName is null) continue;

            var (handlerName, _) = ClassifyHandler(assign.Right);
            result.Add(new CancellationPoint(receiverSymbol, eventName, handlerName));
        }
    }

    /// <summary>从赋值左边提取接收者符号和事件名(支持 MemberAccess 和 ConditionalAccess ?.)</summary>
    private static (ISymbol? ReceiverSymbol, string? EventName) ExtractReceiverAndEvent(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (left is MemberAccessExpressionSyntax ma)
            return (semanticModel.GetSymbolInfo(ma.Expression, ct).Symbol, ma.Name.Identifier.ValueText);
        if (left is ConditionalAccessExpressionSyntax ca && ca.WhenNotNull is MemberBindingExpressionSyntax mb)
            return (semanticModel.GetSymbolInfo(ca.Expression, ct).Symbol, mb.Name.Identifier.ValueText);
        return (null, null);
    }

    /// <summary>递归追踪方法调用链,收集被调用方法体中的 -=</summary>
    private static void TraceCallChainForCancellations(
        SyntaxNode body,
        List<CancellationPoint> result,
        Dictionary<IMethodSymbol, SyntaxNode> allMethods,
        SemanticModel semanticModel,
        CancellationToken ct,
        HashSet<IMethodSymbol> visited) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return;
            var methodSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
            if (methodSymbol is null) continue;
            if (!visited.Add(methodSymbol)) continue;
            if (!allMethods.TryGetValue(methodSymbol, out var methodBody)) continue;
            CollectCancellationsFromBody(methodBody, result, semanticModel, ct);
            TraceCallChainForCancellations(methodBody, result, allMethods, semanticModel, ct, visited);
        }
    }

    private static (string? HandlerName, bool IsLambda) ClassifyHandler(ExpressionSyntax handler) {
        if (handler is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax)
            return (null, true);
        if (handler is IdentifierNameSyntax id)
            return (id.Identifier.ValueText, false);
        if (handler is MemberAccessExpressionSyntax ma)
            return (ma.Name.Identifier.ValueText, false);
        return (null, true);
    }

    private static bool IsInDisposeBody(SyntaxNode node, HashSet<SyntaxNode> disposeBodies) {
        foreach (var ancestor in node.Ancestors()) {
            if (disposeBodies.Contains(ancestor)) return true;
        }
        return false;
    }

    /// <summary>检查节点是否在事件访问器(add/remove)内 — 事件转发,不是订阅</summary>
    private static bool IsInEventAccessor(SyntaxNode node) {
        foreach (var ancestor in node.Ancestors()) {
            if (ancestor is AccessorDeclarationSyntax accessor
                && (accessor.IsKind(SyntaxKind.AddAccessorDeclaration)
                    || accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration)))
                return true;
        }
        return false;
    }

    /// <summary>检查节点是否位于嵌套类型内 — 嵌套类型会被单独分析,外层类不应重复报告其订阅</summary>
    private static bool IsInNestedType(SyntaxNode node, TypeDeclarationSyntax currentType) {
        foreach (var ancestor in node.Ancestors()) {
            if (ancestor == currentType) return false;
            if (ancestor is TypeDeclarationSyntax) return true;
        }
        return false;
    }

    /// <summary>检查同一方法体内是否有对应的 -= (finally 局部配对,非泄露)</summary>
    private static bool IsCancelledInSameMethod(
        AssignmentExpressionSyntax addAssign,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var method = addAssign.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null || method.Body is null) return false;

        var (subReceiver, subEvent, _) = ExtractSubscriptionReceiver(addAssign.Left, semanticModel, ct);
        if (subEvent is null) return false;
        var (subHandler, _) = ClassifyHandler(addAssign.Right);

        foreach (var subAssign in method.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (ct.IsCancellationRequested) return false;
            if (!subAssign.IsKind(SyntaxKind.SubtractAssignmentExpression)) continue;
            var (cancelReceiver, cancelEvent) = ExtractReceiverAndEvent(subAssign.Left, semanticModel, ct);
            if (cancelEvent is null) continue;
            if (!string.Equals(cancelEvent, subEvent, StringComparison.Ordinal)) continue;
            var (cancelHandler, _) = ClassifyHandler(subAssign.Right);
            if (!string.Equals(cancelHandler, subHandler, StringComparison.Ordinal)) continue;
            if (SymbolEqualityComparer.Default.Equals(cancelReceiver, subReceiver)) return true;
        }
        return false;
    }

    /// <summary>检查类中任意方法体内是否有对应的 -= (跨方法参数配对,如 Watch/Unwatch、WireEvents/UnwireEvents)</summary>
    private static bool IsCancelledInAnyMethod(
        AssignmentExpressionSyntax addAssign,
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        CancellationToken ct) {
        var (subReceiver, subEvent, _) = ExtractSubscriptionReceiver(addAssign.Left, semanticModel, ct);
        if (subEvent is null) return false;
        if (subReceiver is not IParameterSymbol) return false;
        var (subHandler, _) = ClassifyHandler(addAssign.Right);

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>()) {
            if (method.Body is null) continue;
            foreach (var subAssign in method.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
                if (ct.IsCancellationRequested) return false;
                if (!subAssign.IsKind(SyntaxKind.SubtractAssignmentExpression)) continue;
                var (cancelReceiver, cancelEvent) = ExtractReceiverAndEvent(subAssign.Left, semanticModel, ct);
                if (cancelEvent is null) continue;
                if (!string.Equals(cancelEvent, subEvent, StringComparison.Ordinal)) continue;
                var (cancelHandler, _) = ClassifyHandler(subAssign.Right);
                if (!string.Equals(cancelHandler, subHandler, StringComparison.Ordinal)) continue;
                if (cancelReceiver is IParameterSymbol) return true;
            }
        }
        return false;
    }
}
