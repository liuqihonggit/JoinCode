using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
/// 生成器公共工具 — 特性扫描的通用 VisitNamespaces 实现
/// 所有 Prompt 相关生成器共享此代码，避免重复
/// </summary>
public static class AttributeScanner
{
    /// <summary>
    /// 扫描当前程序集中所有标记了指定特性的类型
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> ScanTypesWithAttribute(
        Compilation compilation,
        string attributeFullName)
    {
        var attrSymbol = compilation.GetTypeByMetadataName(attributeFullName);
        if (attrSymbol is null)
            return ImmutableArray<INamedTypeSymbol>.Empty;

        var results = new List<INamedTypeSymbol>();
        VisitNamespaces(compilation.GlobalNamespace, compilation.Assembly, attrSymbol, results);
        return results.ToImmutableArray();
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol currentAssembly,
        INamedTypeSymbol attrSymbol,
        List<INamedTypeSymbol> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, currentAssembly, attrSymbol, results);
            else if (member is INamedTypeSymbol typeSymbol
                     && SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, currentAssembly))
            {
                var attr = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));

                if (attr is not null)
                    results.Add(typeSymbol);
            }
        }
    }

    /// <summary>
    /// 获取类型的指定特性数据
    /// </summary>
    public static AttributeData? GetAttribute(INamedTypeSymbol typeSymbol, INamedTypeSymbol attrSymbol)
    {
        return typeSymbol.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));
    }

    /// <summary>
    /// 检查类型是否有指定的无参静态方法
    /// </summary>
    public static bool HasParameterlessStaticMethod(INamedTypeSymbol typeSymbol, string methodName)
    {
        return typeSymbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Any(m => m.IsStatic && m.Parameters.Length == 0 && m.ReturnType.SpecialType != SpecialType.System_Void);
    }

    /// <summary>
    /// 检查类型是否有指定的静态字符串字段
    /// </summary>
    public static bool HasStaticStringField(INamedTypeSymbol typeSymbol, string fieldName)
    {
        return typeSymbol.GetMembers(fieldName)
            .OfType<IFieldSymbol>()
            .Any(f => f.IsStatic && f.Type.SpecialType == SpecialType.System_String);
    }

    /// <summary>
    /// 获取特性的命名参数值（string 类型）
    /// </summary>
    public static string? GetStringNamedArg(AttributeData attr, string name)
    {
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == name)
                return kvp.Value.Value as string;
        }
        return null;
    }

    /// <summary>
    /// 获取特性的命名参数值（int 类型）
    /// </summary>
    public static int GetIntNamedArg(AttributeData attr, string name, int defaultValue = 0)
    {
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == name && kvp.Value.Value is int i)
                return i;
        }
        return defaultValue;
    }

    /// <summary>
    /// 获取特性的命名参数值（bool 类型）
    /// </summary>
    public static bool GetBoolNamedArg(AttributeData attr, string name, bool defaultValue = false)
    {
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == name && kvp.Value.Value is bool b)
                return b;
        }
        return defaultValue;
    }

    /// <summary>
    /// 获取特性的命名参数值（string[] 类型）
    /// </summary>
    public static string[] GetStringArrayNamedArg(AttributeData attr, string name)
    {
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == name)
            {
                return kvp.Value.Values
                    .Select(v => v.Value as string ?? "")
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToArray();
            }
        }
        return [];
    }
}
