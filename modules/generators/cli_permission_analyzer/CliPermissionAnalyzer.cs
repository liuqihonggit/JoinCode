namespace CliPermission.Analyzer;

/// <summary>
/// CLI 参数权限分析器 — ADR 0168：源码真实读取的参数与内嵌策略 YAML 必须双向一致。
/// <para>当前覆盖 local <c>CliNodeHandler</c> 注册表与参数字典读取；MCP/ChatCommand/CliOption
/// 声明源在同一分析器中按所有者项目逐步接入。</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CliPermissionAnalyzer : DiagnosticAnalyzer {
    private const string HandlerMetadataName = "JoinCode.Abstractions.Tools.CliNodeHandler";

    private static readonly DiagnosticDescriptor SourceParameterMissing = new(
        id: "JCC13001",
        title: "CLI 参数缺少权限登记",
        messageFormat: "CLI 工具 '{0}' 的参数 '{1}' 在策略 '{2}' 中没有登记（有参无权）",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "源码实际读取的 CLI 参数必须在嵌入权限 YAML 中登记；新增参数时不能静默落回默认放行.");

    private static readonly DiagnosticDescriptor PolicyParameterUnused = new(
        id: "JCC13002",
        title: "权限策略存在未使用参数",
        messageFormat: "CLI 工具 '{0}' 的策略参数 '{1}' 未被本地处理器读取（有权无参）",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "策略中登记的本地参数必须对应真实读取，避免改名后留下失效权限条目.");

    private static readonly DiagnosticDescriptor DynamicParameterKey = new(
        id: "JCC13004",
        title: "CLI 参数键无法静态证明",
        messageFormat: "CLI 工具 '{0}' 使用不可证明的动态参数键 '{1}'；改用字符串字面量或结构化参数声明",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "动态参数键无法与权限 YAML 对账，新增参数会绕过编译期门禁.");

    private static readonly DiagnosticDescriptor MissingPolicy = new(
        id: "JCC13008",
        title: "CLI 工具缺少权限策略",
        messageFormat: "未找到内嵌 CLI 权限策略，工具 '{0}' 的参数无法对账；在 AdditionalFiles 中加入 policy.yaml",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "local CLI 工具必须有可对账的策略文件，否则权限校验无法覆盖.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SourceParameterMissing, PolicyParameterUnused, DynamicParameterKey, MissingPolicy);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context) {
        var handlerType = context.Compilation.GetTypeByMetadataName(HandlerMetadataName);
        if (handlerType is null) return;

        var policy = PolicyIndex.Load(context.Options.AdditionalFiles, context.CancellationToken);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeHandlerRegistration(nodeContext, handlerType, policy),
            SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeHandlerRegistration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol handlerType,
        PolicyIndex policy) {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is not ImplicitElementAccessSyntax elementAccess
            || elementAccess.ArgumentList.Arguments.Count != 1) {
            return;
        }
        var keyExpression = elementAccess.ArgumentList.Arguments[0].Expression;
        var constant = context.SemanticModel.GetConstantValue(keyExpression, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string toolId || string.IsNullOrWhiteSpace(toolId)) {
            return;
        }
        if (!IsHandlerDictionary(assignment, handlerType, context.SemanticModel, context.CancellationToken)) {
            return;
        }
        if (context.SemanticModel.GetSymbolInfo(assignment.Right, context.CancellationToken).Symbol is not IMethodSymbol handlerMethod) {
            return;
        }

        var builder = new HandlerSummaryBuilder(context.Compilation, context.CancellationToken);
        var summary = builder.Build(handlerMethod);
        ReportDynamicKeys(context, toolId, summary);

        if (policy.IsEmpty) {
            context.ReportDiagnostic(Diagnostic.Create(MissingPolicy, keyExpression.GetLocation(), toolId));
            return;
        }

        if (!policy.Tools.TryGetValue(toolId, out var policyTool)) {
            context.ReportDiagnostic(Diagnostic.Create(
                SourceParameterMissing,
                keyExpression.GetLocation(),
                toolId,
                "<工具缺失>",
                policy.PolicyId));
            return;
        }

        ReportSourceOnlyParameters(context, toolId, policy.PolicyId, summary, policyTool);
        ReportPolicyOnlyParameters(context, toolId, summary, policyTool);
    }

    private static void ReportDynamicKeys(
        SyntaxNodeAnalysisContext context,
        string toolId,
        MethodSummary summary) {
        foreach (var location in summary.DynamicLocations) {
            var expression = location.SourceTree?.GetText(context.CancellationToken).ToString(location.SourceSpan)
                ?? "<动态表达式>";
            context.ReportDiagnostic(Diagnostic.Create(DynamicParameterKey, location, toolId, expression));
        }
    }

    private static void ReportSourceOnlyParameters(
        SyntaxNodeAnalysisContext context,
        string toolId,
        string policyId,
        MethodSummary summary,
        PolicyToolEntry policyTool) {
        foreach (var pair in summary.Parameters) {
            if (policyTool.Parameters.ContainsKey(pair.Key)) continue;
            context.ReportDiagnostic(Diagnostic.Create(
                SourceParameterMissing,
                pair.Value,
                toolId,
                pair.Key,
                policyId));
        }
    }

    private static void ReportPolicyOnlyParameters(
        SyntaxNodeAnalysisContext context,
        string toolId,
        MethodSummary summary,
        PolicyToolEntry policyTool) {
        foreach (var pair in policyTool.Parameters) {
            if (summary.Parameters.ContainsKey(pair.Key)) continue;
            if (pair.Value.Effect == PolicyEffect.Deny) continue;
            context.ReportDiagnostic(Diagnostic.Create(
                PolicyParameterUnused,
                pair.Value.Location,
                toolId,
                pair.Key));
        }
    }

    private static bool IsHandlerDictionary(
        AssignmentExpressionSyntax assignment,
        INamedTypeSymbol handlerType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var initializer = assignment.Parent as InitializerExpressionSyntax;
        var creation = initializer?.Parent as BaseObjectCreationExpressionSyntax;
        if (creation is null) return false;
        var type = semanticModel.GetTypeInfo(creation, cancellationToken).Type as INamedTypeSymbol;
        return type is not null
            && type.TypeArguments.Length == 2
            && SymbolEqualityComparer.Default.Equals(type.TypeArguments[1], handlerType);
    }

    /// <summary>方法参数读取摘要 — 支持 helper 链的参数键传递，不把工具名/普通字符串误当参数。</summary>
    internal sealed class MethodSummary {
        public Dictionary<string, Location> Parameters { get; } = new(StringComparer.Ordinal);

        public HashSet<int> KeyParameterIndices { get; } = new();

        public List<Location> DynamicLocations { get; } = new();
    }

    internal sealed class HandlerSummaryBuilder {
        private readonly Compilation _compilation;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<IMethodSymbol, MethodSummary> _cache =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _inProgress =
            new(SymbolEqualityComparer.Default);

        public HandlerSummaryBuilder(Compilation compilation, CancellationToken cancellationToken) {
            _compilation = compilation;
            _cancellationToken = cancellationToken;
        }

        public MethodSummary Build(IMethodSymbol method) {
            method = method.OriginalDefinition;
            if (_cache.TryGetValue(method, out var cached)) return cached;
            if (!_inProgress.Add(method)) return new MethodSummary();

            var summary = new MethodSummary();
            foreach (var reference in method.DeclaringSyntaxReferences) {
                _cancellationToken.ThrowIfCancellationRequested();
                var syntax = reference.GetSyntax(_cancellationToken);
                var semanticModel = _compilation.GetSemanticModel(syntax.SyntaxTree);
                var optionsParameters = OptionsParameterSymbols(method);
                if (optionsParameters.Count == 0) continue;

                foreach (var node in syntax.DescendantNodes()) {
                    AnalyzeNode(node, semanticModel, optionsParameters, summary);
                }
            }

            _inProgress.Remove(method);
            _cache[method] = summary;
            return summary;
        }

        private void AnalyzeNode(
            SyntaxNode node,
            SemanticModel semanticModel,
            HashSet<IParameterSymbol> optionsParameters,
            MethodSummary summary) {
            if (node is ElementAccessExpressionSyntax elementAccess) {
                if (IsOptionsSymbol(semanticModel, elementAccess.Expression, optionsParameters)
                    && elementAccess.ArgumentList.Arguments.Count == 1) {
                    ClassifyKey(
                        elementAccess.ArgumentList.Arguments[0].Expression,
                        semanticModel,
                        summary);
                }
                return;
            }

            if (node is not InvocationExpressionSyntax invocation) return;
            var target = semanticModel.GetSymbolInfo(invocation, _cancellationToken).Symbol as IMethodSymbol;
            if (target is null) return;

            if (target.Name is "TryGetValue" or "ContainsKey" or "GetValueOrDefault"
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && IsOptionsSymbol(semanticModel, memberAccess.Expression, optionsParameters)
                && invocation.ArgumentList.Arguments.Count > 0) {
                ClassifyKey(invocation.ArgumentList.Arguments[0].Expression, semanticModel, summary);
                return;
            }

            if (!PassesOptionsParameter(invocation, semanticModel, optionsParameters)) return;
            var helperSummary = Build(target.ReducedFrom ?? target);
            // helper 内部已解析出的参数也要并入调用方 —— 两层转发（handler → 骨架 → Required）
            // 只传递 KeyParameterIndices 会把中间层解析好的字面量丢在上一层。
            foreach (var pair in helperSummary.Parameters) {
                if (!summary.Parameters.ContainsKey(pair.Key)) {
                    summary.Parameters.Add(pair.Key, pair.Value);
                }
            }
            foreach (var location in helperSummary.DynamicLocations) {
                if (!summary.DynamicLocations.Any(existing => existing.SourceSpan == location.SourceSpan)) {
                    summary.DynamicLocations.Add(location);
                }
            }
            foreach (var parameterIndex in helperSummary.KeyParameterIndices) {
                var argument = ArgumentForParameterOrdinal(invocation, target, parameterIndex);
                if (argument is null) continue;
                ClassifyKey(argument.Expression, semanticModel, summary);
            }
        }

        private static void ClassifyKey(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            MethodSummary summary) {
            var constant = semanticModel.GetConstantValue(expression, CancellationToken.None);
            if (constant.HasValue && constant.Value is string literal && literal.Length > 0) {
                if (!summary.Parameters.ContainsKey(literal)) {
                    summary.Parameters.Add(literal, expression.GetLocation());
                }
                return;
            }

            if (semanticModel.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter
                && parameter.Type.SpecialType == SpecialType.System_String) {
                summary.KeyParameterIndices.Add(parameter.Ordinal);
                return;
            }

            summary.DynamicLocations.Add(expression.GetLocation());
        }

        private static ArgumentSyntax? ArgumentForParameterOrdinal(
            InvocationExpressionSyntax invocation,
            IMethodSymbol target,
            int parameterOrdinal) {
            var arguments = invocation.ArgumentList.Arguments;
            if (parameterOrdinal < 0 || parameterOrdinal >= target.Parameters.Length) return null;
            var parameterName = target.Parameters[parameterOrdinal].Name;
            foreach (var argument in arguments) {
                if (argument.NameColon is not null) {
                    if (string.Equals(argument.NameColon.Name.Identifier.ValueText, parameterName, StringComparison.Ordinal)) {
                        return argument;
                    }
                    continue;
                }
            }

            var positionalIndex = 0;
            foreach (var argument in arguments) {
                if (argument.NameColon is not null) continue;
                if (positionalIndex == parameterOrdinal) return argument;
                positionalIndex++;
            }
            return null;
        }

        private static bool PassesOptionsParameter(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            HashSet<IParameterSymbol> optionsParameters) {
            foreach (var argument in invocation.ArgumentList.Arguments) {
                if (IsOptionsSymbol(semanticModel, argument.Expression, optionsParameters)) return true;
            }
            return false;
        }

        private static bool IsOptionsSymbol(
            SemanticModel semanticModel,
            ExpressionSyntax expression,
            HashSet<IParameterSymbol> optionsParameters) =>
            semanticModel.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter
            && optionsParameters.Contains(parameter);

        private static HashSet<IParameterSymbol> OptionsParameterSymbols(IMethodSymbol method) {
            var result = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            foreach (var parameter in method.Parameters) {
                if (parameter.Type is INamedTypeSymbol type
                    && type.TypeArguments.Length == 2
                    && type.TypeArguments[0].SpecialType == SpecialType.System_String
                    && type.TypeArguments[1].SpecialType == SpecialType.System_String
                    && type.Name is "IReadOnlyDictionary" or "IDictionary" or "Dictionary") {
                    result.Add(parameter);
                }
            }
            return result;
        }
    }
}
