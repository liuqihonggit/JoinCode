using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace McpToolHandlers.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegisterAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string RegisterAttributeFullName = "JoinCode.Abstractions.Attributes.RegisterAttribute";

    private static readonly DiagnosticDescriptor RuleMultiInterfaceWithoutExplicitType = new(
        "JCC4001",
        "[Register] 缺少显式接口类型",
        "类 '{0}' 实现了 {1} 个业务接口，但 [Register] 未显式指定接口类型。请使用 [Register(typeof(IFoo), typeof(IBar))] 显式指定。",
        "DIServiceRegistration",
        DiagnosticSeverity.Error,
        true,
        "When a class implements multiple business interfaces (excluding IDisposable/IAsyncDisposable), you must explicitly specify the interface types in [Register] to avoid unintended auto-registration.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleMultiInterfaceWithoutExplicitType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // 只检查类
        if (typeSymbol.TypeKind != TypeKind.Class)
            return;

        // 查找 [Register] 特性
        var registerAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RegisterAttributeFullName);

        if (registerAttr is null)
            return;

        // 检查是否显式指定了接口类型
        var hasExplicitType = registerAttr.ConstructorArguments.Any(arg =>
        {
            if (arg.Value is INamedTypeSymbol typeSym)
            {
                var typeName = typeSym.ToDisplayString();
                return !typeName.Contains("ServiceLifetime") && !typeName.Contains("JoinCode.Abstractions.Attributes");
            }
            return false;
        });

        if (hasExplicitType)
            return; // 已显式指定，不需要检查

        // 统计业务接口数量（排除 IDisposable/IAsyncDisposable 和管道接口）
        var businessInterfaces = typeSymbol.AllInterfaces
            .Where(i => i.Name != "IDisposable" && i.Name != "IAsyncDisposable")
            .Where(i => !i.ToDisplayString().Contains("IMiddleware<") && !i.ToDisplayString().Contains("IStreamMiddleware<"))
            .ToList();

        if (businessInterfaces.Count > 1)
        {
            var interfaceNames = string.Join(", ", businessInterfaces.Select(i => i.Name));
            var diagnostic = Diagnostic.Create(
                RuleMultiInterfaceWithoutExplicitType,
                typeSymbol.Locations[0],
                typeSymbol.Name,
                businessInterfaces.Count,
                interfaceNames);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
