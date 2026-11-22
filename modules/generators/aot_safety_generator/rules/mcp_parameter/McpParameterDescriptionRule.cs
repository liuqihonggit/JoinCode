namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC2010: MCP 工具参数描述验证 — [McpTool] 方法的每个业务参数(非 CancellationToken/[McpToolOptions])
/// 必须有 [McpToolParameter] 特性,且描述非空或用 WellKnownParam 构造。空描述→编译报错。
/// <para>互补于 JCC10010(WellKnownParamStringRule):JCC10010 检测"重复描述建议改 WellKnownParam",JCC2010 检测"缺失描述"。</para>
/// <para>构造函数签名参考 McpToolParameterAttribute:(string description) 手写描述 / (WellKnownParam wellKnown) 枚举填充。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "McpParam",
    Id = "JCC2010",
    Title = "MCP 参数描述缺失: {0}",
    Description = "[McpTool] 方法的参数 '{0}' 缺少有效描述。每个业务参数(非 CancellationToken/[McpToolOptions])必须有 [McpToolParameter] 特性,且描述非空或用 WellKnownParam 构造。空描述导致 AI 调用时不知道参数含义,换命令绕过比直接拒绝更危险。",
    Category = "McpContract",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "MCP 工具参数必须有描述. 1) 手写描述: [McpToolParameter(\"参数含义\")]; 2) 公共参数用枚举: [McpToolParameter(WellKnownParam.FilePath)]. 禁止 [McpToolParameter(\"\")] 空描述. ADR: 0135")]
public sealed class McpParameterDescriptionRule : AnalyzerRuleBase<McpParameterDescriptionRule> {
    private const string McpToolAttributeName = "McpTool";
    private const string McpToolParameterAttributeName = "McpToolParameter";
    private const string McpToolOptionsAttributeName = "McpToolOptions";
    private const string CancellationTokenTypeName = "CancellationToken";

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var method = (MethodDeclarationSyntax)ctx.Node;
            var parameterList = method.ParameterList;
            if (parameterList is null) return;

            // 快速判断方法是否有 [McpTool] 特性,无则跳过
            if (!HasMcpToolAttribute(method)) return;

            foreach (var param in parameterList.Parameters) {
                if (ctx.CancellationToken.IsCancellationRequested) return;
                CheckParameter(ctx, param);
            }
        }, SyntaxKind.MethodDeclaration);
    }

    private static bool HasMcpToolAttribute(MethodDeclarationSyntax method) {
        foreach (var attrList in method.AttributeLists) {
            foreach (var attr in attrList.Attributes) {
                var name = attr.Name.ToString().Replace(" ", "");
                if (name == McpToolAttributeName || name == McpToolAttributeName + "Attribute")
                    return true;
            }
        }
        return false;
    }

    private static void CheckParameter(SyntaxNodeAnalysisContext ctx, ParameterSyntax param) {
        // 跳过 CancellationToken 参数(特殊参数,不需描述)
        var paramType = param.Type?.ToString().Replace(" ", "");
        if (paramType == CancellationTokenTypeName) return;

        // 跳过回调/进度参数(ToolProgressCallback/IProgress<T>/Action<T> 等,非 MCP 工具参数)
        if (IsCallbackType(paramType)) return;

        // 跳过 [McpToolOptions] 参数(选项类参数,不需 [McpToolParameter])
        if (HasAttribute(param, McpToolOptionsAttributeName)) return;

        // 查找 [McpToolParameter] 特性
        var mcpParamAttr = FindAttribute(param, McpToolParameterAttributeName);
        if (mcpParamAttr is null) {
            // 无 [McpToolParameter] 特性 — 报错
            Report(ctx, param, param.Identifier.ValueText);
            return;
        }

        // 检查 [McpToolParameter] 第一个参数
        var args = mcpParamAttr.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0) {
            // 无构造参数 — 报错(构造函数要求至少一个参数,这是编译错误,但分析器也报)
            Report(ctx, mcpParamAttr, param.Identifier.ValueText);
            return;
        }

        var firstArg = args.Value[0].Expression;
        // 如果第一个参数是 WellKnownParam 枚举成员 → OK(生成器从 ParamMeta 填充描述)
        if (IsWellKnownParamExpression(firstArg)) return;

        // 如果第一个参数是字符串字面量
        if (firstArg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) {
            var description = literal.Token.ValueText;
            if (string.IsNullOrWhiteSpace(description)) {
                // 空描述 — 报错
                Report(ctx, mcpParamAttr, param.Identifier.ValueText);
            }
            return;
        }

        // 其他表达式(枚举/nameof 等)— 无法静态判断,跳过
    }

    private static AttributeSyntax? FindAttribute(ParameterSyntax param, string attributeName) {
        foreach (var attrList in param.AttributeLists) {
            foreach (var attr in attrList.Attributes) {
                var name = attr.Name.ToString().Replace(" ", "");
                if (name == attributeName || name == attributeName + "Attribute")
                    return attr;
            }
        }
        return null;
    }

    private static bool HasAttribute(ParameterSyntax param, string attributeName) {
        return FindAttribute(param, attributeName) is not null;
    }

    private static bool IsWellKnownParamExpression(ExpressionSyntax expr) {
        // WellKnownParam.Xxx 形式: MemberAccessExpression
        if (expr is MemberAccessExpressionSyntax memberAccess) {
            var typeText = memberAccess.Expression?.ToString().Replace(" ", "");
            return typeText == "WellKnownParam";
        }
        // 直接枚举值(罕见) — 无法确定类型,跳过
        return false;
    }

    /// <summary>
    /// 判断是否为回调/进度类型(ToolProgressCallback/IProgress&lt;T&gt;/Action&lt;T&gt;/Func&lt;...&gt; 等) — 非 MCP 工具参数,跳过描述检查。
    /// </summary>
    private static bool IsCallbackType(string? paramType) {
        if (paramType is null) return false;
        // 去掉可空标记 ? 和泛型参数,只看类型名
        var coreType = paramType.TrimEnd('?');
        // ToolProgressCallback、IProgress<T>、Action<T>、Func<...>、EventHandler<T> 等
        return coreType.Contains("Callback")
            || coreType.Contains("Progress")
            || coreType.StartsWith("Action<", StringComparison.Ordinal)
            || coreType.StartsWith("Func<", StringComparison.Ordinal)
            || coreType.StartsWith("EventHandler", StringComparison.Ordinal);
    }

    private static void Report(SyntaxNodeAnalysisContext ctx, SyntaxNode locationNode, string paramName) {
        var location = locationNode.GetLocation();
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, paramName));
    }
}
