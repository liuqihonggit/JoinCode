namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9312: ModuleInitializer 中央登记 — 非测试程序集必须显式声明
/// <c>&lt;JccAllowModuleInitializer&gt;true&lt;/JccAllowModuleInitializer&gt;</c>。
/// <para>背景: CA2255 原先由每个项目/每个文件自行豁免（Infrastructure 项目级 NoWarn + 17 份测试 AssemblyInfo 的
/// <c>#pragma warning disable CA2255</c>），新增引导代码没有统一门禁。根目录已全局豁免 CA2255，由本规则接管：
/// 测试程序集默认放行（UTF-8/本地化引导是测试惯例），非测试程序集必须 csproj 显式登记并写理由。</para>
/// <para>修复导向: 在项目 csproj 加 <c>&lt;JccAllowModuleInitializer&gt;true&lt;/JccAllowModuleInitializer&gt;</c>
/// 并注释为什么必须随程序集加载自动执行；确属测试辅助程序集则设 <c>&lt;ProjectType&gt;test&lt;/ProjectType&gt;</c>。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC9312",
    Title = "ModuleInitializer 需中央登记",
    Description = "非测试程序集使用 [ModuleInitializer] '{0}' 未经登记。在 csproj 声明 <JccAllowModuleInitializer>true</JccAllowModuleInitializer> 并注释理由（中央登记替代散落 #pragma/NoWarn）；测试程序集默认放行。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "[ModuleInitializer] in a non-test assembly must be registered centrally: set <JccAllowModuleInitializer>true</JccAllowModuleInitializer> in the csproj together with a comment explaining why it must run on assembly load. Test assemblies (ProjectType=test) are allowed by convention. This replaces per-file '#pragma warning disable CA2255' and project-level NoWarn.")]
public sealed class ModuleInitializerGovernanceRule : AnalyzerRuleBase<ModuleInitializerGovernanceRule> {
    private const string AttributeFullName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        // 测试程序集放行；非测试程序集已登记放行；其余才需要拦截
        if (projectContext.IsTest || projectContext.AllowModuleInitializer) return;

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var method = (MethodDeclarationSyntax)ctx.Node;
        foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes)) {
            if (ctx.SemanticModel.GetSymbolInfo(attribute, ctx.CancellationToken).Symbol is not IMethodSymbol ctor) continue;
            if (ctor.ContainingType?.ToDisplayString() != AttributeFullName) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                attribute.GetLocation(),
                $"{method.Identifier.Text}"));
            return;
        }
    }
}
