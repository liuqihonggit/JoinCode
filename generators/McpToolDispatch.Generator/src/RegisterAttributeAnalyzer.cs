using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace McpToolDispatch.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegisterAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string RegisterAttributeFullName = "JoinCode.Abstractions.Attributes.RegisterAttribute";
    private const string AllowSkipEntityAttributeFullName = "JoinCode.Abstractions.Attributes.AllowSkipEntityAttribute";
    private const string EntityFullName = "JoinCode.Abstractions.Entity.Entity";
    private const string ServiceEntityFullName = "JoinCode.Abstractions.Entity.ServiceEntity";

    private static readonly DiagnosticDescriptor RuleMultiInterfaceWithoutExplicitType = new(
        "JCC4010",
        "[Register] 缺少显式接口类型",
        "类 '{0}' 实现了 {1} 个业务接口，但 [Register] 未显式指定接口类型。请使用 [Register(typeof(IFoo), typeof(IBar))] 显式指定。",
        "DIServiceRegistration",
        DiagnosticSeverity.Error,
        true,
        "When a class implements multiple business interfaces (excluding IDisposable/IAsyncDisposable), you must explicitly specify the interface types in [Register] to avoid unintended auto-registration.");

    private static readonly DiagnosticDescriptor RuleMustInheritEntity = new(
        "JCC4012",
        "[Register] 类必须继承 ServiceEntity",
        "类 '{0}' 标记了 [Register] 但未继承 ServiceEntity/Entity. 请继承 ServiceEntity 获得 ObjectId 生命周期追踪, 或用 [AllowSkipEntity(\"原因\")] 豁免.",
        "DIServiceRegistration",
        DiagnosticSeverity.Error,
        true,
        "All DI services must inherit ServiceEntity to get ObjectId lifecycle tracking. Use [AllowSkipEntity] for exemption (e.g. record types, IAsyncDisposable conflict).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleMultiInterfaceWithoutExplicitType, RuleMustInheritEntity);

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

        // 统计业务接口数量 — 只看类直接声明的接口(Interfaces), 不看从基类继承的(AllInterfaces)
        // 基类继承的接口(如 Entity 的 ICloneableEntity/IDisposable)不算派生类的业务接口
        var businessInterfaces = typeSymbol.Interfaces
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

        // JCC4012: [Register] 类必须继承 ServiceEntity/Entity（除非 [AllowSkipEntity] 豁免）
        var hasAllowSkip = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == AllowSkipEntityAttributeFullName);
        if (hasAllowSkip)
            return;

        // record 不适合继承 ServiceEntity（Equals/GetHashCode 语义冲突），跳过检查
        if (typeSymbol.IsRecord)
            return;

        // 检查基类链是否包含 Entity 或 ServiceEntity
        var baseChain = typeSymbol.BaseType;
        var hasEntityBase = false;
        while (baseChain is not null)
        {
            var baseFullName = baseChain.ToDisplayString();
            if (baseFullName == EntityFullName || baseFullName == ServiceEntityFullName)
            {
                hasEntityBase = true;
                break;
            }
            baseChain = baseChain.BaseType;
        }

        if (!hasEntityBase)
        {
            var diagnostic = Diagnostic.Create(
                RuleMustInheritEntity,
                typeSymbol.Locations[0],
                typeSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
