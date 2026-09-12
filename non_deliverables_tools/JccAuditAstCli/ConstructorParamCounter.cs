using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JccAuditCli;

/// <summary>
/// 构造函数参数计数检测器：扫描所有类的构造函数，找出参数数量超过阈值的构造函数
/// 用于识别可重构为中间件模式的高耦合类
/// </summary>
public static class ConstructorParamCounter
{
    /// <summary>
    /// 默认参数数量阈值：超过此值视为"胖构造函数"，建议重构
    /// </summary>
    public const int DefaultThreshold = 8;

    /// <summary>
    /// 从 Compilation 中提取所有参数数量超过阈值的构造函数
    /// </summary>
    /// <param name="compilation">Roslyn Compilation</param>
    /// <param name="threshold">参数数量阈值（默认 8）</param>
    public static List<ConstructorParamInfo> Extract(Compilation compilation, int threshold = DefaultThreshold)
    {
        var results = new List<ConstructorParamInfo>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var filePath = syntaxTree.FilePath ?? string.Empty;
            if (filePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                filePath.Contains("/obj/", StringComparison.Ordinal))
                continue;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            ExtractFromTree(syntaxTree, semanticModel, filePath, threshold, results);
        }

        return results;
    }

    private static void ExtractFromTree(SyntaxTree syntaxTree, SemanticModel semanticModel,
        string filePath, int threshold, List<ConstructorParamInfo> results)
    {
        var root = syntaxTree.GetRoot();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            // 跳过抽象类和接口（接口不会有构造函数，但防御性检查）
            if (classDecl.Modifiers.Any(m => m.Text == "static"))
                continue;

            var className = classDecl.Identifier.ToString();
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol is null || classSymbol.IsAbstract)
                continue;

            var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ShouldSkipClass(className, ns))
                continue;

            // 找出参数最多的构造函数（一个类可能有多个构造函数）
            ConstructorDeclarationSyntax? fattestCtor = null;
            var maxParamCount = 0;

            foreach (var ctor in classDecl.Members.OfType<ConstructorDeclarationSyntax>())
            {
                var parameters = ctor.ParameterList?.Parameters;
                if (parameters is null)
                    continue;

                // 过滤基础设施类型后的有效参数数量
                var effectiveCount = parameters.Value.Count(p =>
                {
                    var paramType = p.Type?.ToString() ?? string.Empty;
                    return !IsInfrastructureType(paramType);
                });

                if (effectiveCount > maxParamCount)
                {
                    maxParamCount = effectiveCount;
                    fattestCtor = ctor;
                }
            }

            if (fattestCtor is null || maxParamCount <= threshold)
                continue;

            // 提取参数类型列表（过滤基础设施类型）
            var paramTypes = new List<string>();
            var allParams = fattestCtor.ParameterList!.Parameters;
            foreach (var param in allParams)
            {
                var paramType = param.Type?.ToString() ?? "object";
                if (!IsInfrastructureType(paramType))
                {
                    paramTypes.Add(paramType);
                }
            }

            var lineInfo = fattestCtor.GetLocation().GetLineSpan();
            var signature = BuildSignature(className, allParams);

            results.Add(new ConstructorParamInfo(
                className,
                filePath,
                lineInfo.StartLinePosition.Line + 1,
                paramTypes.Count,
                paramTypes,
                signature));
        }
    }

    /// <summary>
    /// 判断是否应跳过该类（与 DiRegistrationExtractor 保持一致）
    /// </summary>
    private static bool ShouldSkipClass(string className, string ns)
    {
        return className is "Program" or "Startup" or "Configuration" or "Options" or "Config"
               || className.EndsWith("Attribute", StringComparison.Ordinal)
               || className.EndsWith("Tests", StringComparison.Ordinal)
               || className.EndsWith("Mock", StringComparison.Ordinal)
               || className.EndsWith("Stub", StringComparison.Ordinal)
               || className.EndsWith("Fake", StringComparison.Ordinal)
               || className.EndsWith("Benchmark", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断是否为基础设施类型（不计入有效参数数量）
    /// 与 DiRegistrationExtractor.IsInfrastructureType 保持一致
    /// </summary>
    private static bool IsInfrastructureType(string type)
    {
        return type == "IServiceProvider"
               || type == "IServiceScopeFactory"
               || type == "IConfiguration"
               || type == "IConfigurationSection"
               || type == "IWebHostEnvironment"
               || type == "IHostEnvironment"
               || type.StartsWith("ILogger", StringComparison.Ordinal)
               || type.StartsWith("IOptions", StringComparison.Ordinal);
    }

    /// <summary>
    /// 构造函数签名（用于报告展示）
    /// </summary>
    private static string BuildSignature(string className, SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (var param in parameters)
        {
            var paramType = param.Type?.ToString() ?? "object";
            var paramName = param.Identifier.ToString();
            parts.Add($"{paramType} {paramName}");
        }
        return $"{className}({string.Join(", ", parts)})";
    }
}
