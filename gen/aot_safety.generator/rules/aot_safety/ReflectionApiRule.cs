namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1007/JCC1013-1016: AOT 不兼容的反射 API 调用。
/// 多描述符规则 — 5 个 descriptor 对应 Emit/AssemblyLoad/TypeGetType/ActivatorCreateInstance/MethodInfoInvoke。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1007",
    Title = "AOT 不兼容: System.Reflection.Emit 在 NativeAOT 下不支持",
    Description = "调用 '{0}' 属于 System.Reflection.Emit 命名空间，NativeAOT 不支持运行期 IL 生成。使用源码生成器在编译期生成代码替代。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Error,
    HelpLinkUri = "System.Reflection.Emit 依赖运行期动态生成 IL 方法体, NativeAOT 编译时需要确定所有代码. 替代方案: 1) 使用 Roslyn 源码生成器 (IIncrementalGenerator); 2) 使用表达式树编译 (Expression.Compile) 在 AOT 下也不支持; 3) 改为静态代码生成.")]
public sealed class ReflectionApiRule : IAnalyzerRule {
    private static readonly DiagnosticDescriptor RuleEmit = RuleDescriptorFactory.Create<ReflectionApiRule>();

    private static readonly DiagnosticDescriptor RuleAssemblyLoad = new(
        "JCC1013",
        "AOT 风险: Assembly.Load 在 NativeAOT 下可能失败",
        "调用 '{0}' 在 NativeAOT 下可能因程序集被裁剪而失败。使用静态引用或 Trimmer 根配置替代。",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "Assembly.Load/LoadFrom/Load(AssemblyName) 依赖运行期加载程序集, NativeAOT 裁剪后程序集可能不存在. 替代方案: 1) 使用静态 ProjectReference; 2) 在 csproj 配置 TrimmerRootDescription; 3) 使用 typeof(T).Assembly 获取已知程序集.");

    private static readonly DiagnosticDescriptor RuleTypeGetType = new(
        "JCC1014",
        "AOT 风险: Type.GetType(string) 在 NativeAOT 下可能返回 null",
        "调用 Type.GetType('{0}') 依赖运行期类型解析，NativeAOT 裁剪后目标类型可能被移除。使用 typeof(T) 编译期已知类型替代。",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "Type.GetType(string) 依赖运行期类型名解析, NativeAOT 裁剪后非根类型可能被移除导致返回 null. 替代方案: 1) 使用 typeof(T) 编译期已知; 2) 使用源码生成器生成类型映射; 3) 在 TrimmerRootDescriptor 中保留所需类型.");

    private static readonly DiagnosticDescriptor RuleActivatorCreateInstance = new(
        "JCC1015",
        "AOT 风险: Activator.CreateInstance(Type) 在 NativeAOT 下性能差且可能失败",
        "调用 '{0}' 依赖运行期反射实例化，NativeAOT 下需要类型已注册。使用 new T() 或工厂模式替代。",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "Activator.CreateInstance(Type) 依赖运行期反射构造, NativeAOT 下需要类型在 TrimmerRootDescriptor 中注册. 替代方案: 1) 使用 new T() 直接构造; 2) 使用工厂模式 + switch 分发; 3) 使用源码生成器生成工厂代码.");

    private static readonly DiagnosticDescriptor RuleMethodInfoInvoke = new(
        "JCC1016",
        "AOT 风险: MethodInfo.Invoke 在 NativeAOT 下性能差",
        "调用 '{0}.Invoke' 依赖运行期反射调用，NativeAOT 下性能远不如直接调用。使用直接调用或源码生成器分发替代。",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "MethodInfo.Invoke 依赖运行期方法解析和参数装箱, NativeAOT 下性能远不如直接调用. 替代方案: 1) 使用委托 (Delegate) 或 Func/Action; 2) 使用源码生成器生成分发代码; 3) 使用接口多态替代反射调用.");

    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } =
        new[] { RuleEmit, RuleAssemblyLoad, RuleTypeGetType, RuleActivatorCreateInstance, RuleMethodInfoInvoke };

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not InvocationExpressionSyntax invocation) return;

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null) return;

        var containingNamespace = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var typeName = containingType.Name;
        var methodName = methodSymbol.Name;
        var fullName = $"{containingNamespace}.{typeName}.{methodName}";

        if (containingNamespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)) {
            ctx.ReportDiagnostic(Diagnostic.Create(RuleEmit, invocation.GetLocation(), fullName));
            return;
        }

        if (typeName == "Assembly" && containingNamespace == "System.Reflection" &&
            (methodName == "Load" || methodName == "LoadFrom" || methodName == "LoadFile" ||
             methodName == "LoadWithPartialName" || methodName == "ReflectionOnlyLoad" ||
             methodName == "ReflectionOnlyLoadFrom")) {
            ctx.ReportDiagnostic(Diagnostic.Create(RuleAssemblyLoad, invocation.GetLocation(), fullName));
            return;
        }

        if (typeName == "Type" && containingNamespace == "System" && methodName == "GetType") {
            ctx.ReportDiagnostic(Diagnostic.Create(RuleTypeGetType, invocation.GetLocation(), methodName));
            return;
        }

        if (typeName == "Activator" && containingNamespace == "System" &&
            (methodName == "CreateInstance" || methodName == "CreateInstanceFrom")) {
            ctx.ReportDiagnostic(Diagnostic.Create(RuleActivatorCreateInstance, invocation.GetLocation(), fullName));
            return;
        }

        if (typeName == "MethodInfo" && containingNamespace == "System.Reflection" && methodName == "Invoke") {
            ctx.ReportDiagnostic(Diagnostic.Create(RuleMethodInfoInvoke, invocation.GetLocation(), typeName));
        }
    }
}
