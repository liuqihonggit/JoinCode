
namespace EnumMetadata.Generator;

/// <summary>
/// 枚举元数据源码生成器 — 扫描 [EnumValue] 特性，自动生成 ToValue/FromValue 映射代码
/// </summary>
[Generator]
public sealed class EnumMetadataGenerator : IIncrementalGenerator
{
    private const string EnumValueAttributeFullName = "JoinCode.Abstractions.Attributes.EnumValueAttribute";
    private const string AliasValueAttributeFullName = "JoinCode.Abstractions.Attributes.AliasValueAttribute";
    private const string ModelInfoAttributeFullName = "JoinCode.Abstractions.Attributes.ModelInfoAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enumInfos = context.CompilationProvider
            .SelectMany(static (compilation, _) =>
            {
                var enumValueAttr = compilation.GetTypeByMetadataName(EnumValueAttributeFullName);
                var aliasValueAttr = compilation.GetTypeByMetadataName(AliasValueAttributeFullName);
                var modelInfoAttr = compilation.GetTypeByMetadataName(ModelInfoAttributeFullName);
                if (enumValueAttr is null)
                    return ImmutableArray<EnumInfo>.Empty;

                var results = new List<EnumInfo>();
                VisitNamespaces(compilation.GlobalNamespace, compilation.Assembly, enumValueAttr, aliasValueAttr, modelInfoAttr, results);
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(enumInfos, static (ctx, enums) =>
        {
            foreach (var enumInfo in enums)
            {
                GenerateExtensionClass(ctx, enumInfo);
                if (enumInfo.ModelMembers.Length > 0)
                    GenerateModelEntries(ctx, enumInfo);
            }
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol currentAssembly,
        INamedTypeSymbol enumValueAttr,
        INamedTypeSymbol? aliasValueAttr,
        INamedTypeSymbol? modelInfoAttr,
        List<EnumInfo> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, currentAssembly, enumValueAttr, aliasValueAttr, modelInfoAttr, results);
            else if (member is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                     && SymbolEqualityComparer.Default.Equals(enumType.ContainingAssembly, currentAssembly))
            {
                var members = new List<EnumMemberInfo>();
                var modelMembers = new List<ModelMemberInfo>();
                foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    var attr = field.GetAttributes()
                        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, enumValueAttr));
                    if (attr is not null)
                    {
                        var value = attr.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? field.Name;

                        var aliases = ImmutableArray<string>.Empty;
                        if (aliasValueAttr is not null)
                        {
                            var aliasAttrs = field.GetAttributes()
                                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, aliasValueAttr))
                                .Select(a => a.ConstructorArguments.ElementAtOrDefault(0).Value as string)
                                .Where(a => a is not null)
                                .Cast<string>()
                                .ToImmutableArray();
                            aliases = aliasAttrs;
                        }

                        members.Add(new EnumMemberInfo(field.Name, value, aliases));

                        if (modelInfoAttr is not null)
                        {
                            var miAttr = field.GetAttributes()
                                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, modelInfoAttr));
                            if (miAttr is not null)
                            {
                                var provider = miAttr.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? "";
                                var displayName = miAttr.ConstructorArguments.ElementAtOrDefault(1).Value as string ?? field.Name;
                                var contextWindow = (int)(miAttr.ConstructorArguments.ElementAtOrDefault(2).Value ?? 0);
                                var description = miAttr.ConstructorArguments.ElementAtOrDefault(3).Value as string ?? "";

                                var isDefault = false;
                                var isFastDefault = false;
                                foreach (var named in miAttr.NamedArguments)
                                {
                                    if (named.Key == "IsDefault" && named.Value.Value is bool b1) isDefault = b1;
                                    if (named.Key == "IsFastDefault" && named.Value.Value is bool b2) isFastDefault = b2;
                                }

                                modelMembers.Add(new ModelMemberInfo(field.Name, value, provider, displayName, contextWindow, description, isDefault, isFastDefault));
                            }
                        }
                    }
                }

                if (members.Count > 0)
                {
                    results.Add(new EnumInfo(
                        enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        enumType.Name,
                        enumType.ContainingNamespace.ToDisplayString(),
                        members.ToImmutableArray(),
                        modelMembers.ToImmutableArray()));
                }
            }
        }
    }

    private static void GenerateExtensionClass(SourceProductionContext context, EnumInfo enumInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Frozen;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine($"namespace {enumInfo.Namespace};");
        sb.AppendLine();

        // 生成 const string 常量类 — 替代手写的 XxxConstants 类
        sb.AppendLine($"public static class {enumInfo.Name}Constants");
        sb.AppendLine("{");
        foreach (var member in enumInfo.Members)
        {
            sb.AppendLine($"    public const string {member.Name} = \"{EscapeString(member.Value)}\";");
            foreach (var alias in member.Aliases)
            {
                sb.AppendLine($"    public const string {member.Name}Alias_{EscapeIdentifier(alias)} = \"{EscapeString(alias)}\";");
            }
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // 生成扩展方法类
        sb.AppendLine($"public static class {enumInfo.Name}Extensions");
        sb.AppendLine("{");

        // 正向映射: Enum -> string
        sb.AppendLine($"    private static readonly FrozenDictionary<{enumInfo.FullyQualifiedName}, string> __valueMap = new Dictionary<{enumInfo.FullyQualifiedName}, string>");
        sb.AppendLine("    {");
        foreach (var member in enumInfo.Members)
        {
            sb.AppendLine($"        [{enumInfo.FullyQualifiedName}.{member.Name}] = \"{EscapeString(member.Value)}\",");
        }
        sb.AppendLine("    }.ToFrozenDictionary();");
        sb.AppendLine();

        // 反向映射: string -> Enum（含别名）
        sb.AppendLine($"    private static readonly FrozenDictionary<string, {enumInfo.FullyQualifiedName}> __reverseMap = new Dictionary<string, {enumInfo.FullyQualifiedName}>");
        sb.AppendLine("    {");
        foreach (var member in enumInfo.Members)
        {
            sb.AppendLine($"        [\"{EscapeString(member.Value)}\"] = {enumInfo.FullyQualifiedName}.{member.Name},");
            foreach (var alias in member.Aliases)
            {
                sb.AppendLine($"        [\"{EscapeString(alias)}\"] = {enumInfo.FullyQualifiedName}.{member.Name},");
            }
        }
        sb.AppendLine("    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine();

        // ToValue 扩展方法
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 获取枚举成员的字符串值");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static string ToValue(this {enumInfo.FullyQualifiedName} value)");
        sb.AppendLine($"        => __valueMap.GetValueOrDefault(value, value.ToString().ToLowerInvariant());");
        sb.AppendLine();

        // FromValue 方法
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 从字符串值解析枚举成员");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static {enumInfo.FullyQualifiedName}? FromValue(string? value)");
        sb.AppendLine($"        => value is not null && __reverseMap.TryGetValue(value, out var result) ? result : null;");

        // IsDefined 方法
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 判断枚举值是否为已定义的成员");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static bool IsDefined({enumInfo.FullyQualifiedName} value)");
        sb.AppendLine("        => __valueMap.ContainsKey(value);");

        sb.AppendLine("}");

        context.AddSource($"{enumInfo.Name}Extensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeIdentifier(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        var nextUpper = true;
        foreach (var c in s)
        {
            if (c == '-' || c == '_' || c == '.')
            {
                nextUpper = true;
                continue;
            }
            sb.Append(nextUpper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            nextUpper = false;
        }
        return sb.ToString();
    }

    private static void GenerateModelEntries(SourceProductionContext context, EnumInfo enumInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine($"using JoinCode.Abstractions.Configuration.Llm;");
        sb.AppendLine();
        sb.AppendLine($"namespace {enumInfo.Namespace};");
        sb.AppendLine();

        sb.AppendLine($"public static class {enumInfo.Name}ModelEntries");
        sb.AppendLine("{");

        // 按 Provider 分组
        var groupDict = new Dictionary<string, List<ModelMemberInfo>>();
        foreach (var m in enumInfo.ModelMembers)
        {
            var key = m.Provider.ToLowerInvariant();
            if (!groupDict.TryGetValue(key, out var list))
            {
                list = new List<ModelMemberInfo>();
                groupDict[key] = list;
            }
            list.Add(m);
        }

        var orderedKeys = groupDict.Keys.OrderBy(k => k).ToList();

        foreach (var key in orderedKeys)
        {
            var group = groupDict[key];
            var providerId = EscapeIdentifier(key);
            var pascalProvider = ToPascalCase(key);

            // 生成 ModelEntry[] 数组
            sb.AppendLine($"    public static readonly ModelEntry[] {pascalProvider}Models =");
            sb.AppendLine("    [");
            foreach (var m in group)
            {
                sb.AppendLine($"        new(\"{EscapeString(m.Value)}\", \"{EscapeString(m.DisplayName)}\", {m.ContextWindow}, \"{EscapeString(m.Description)}\"),");
            }
            sb.AppendLine("    ];");
            sb.AppendLine();

            // 生成默认模型
            var defaultModel = group.FirstOrDefault(m => m.IsDefault);
            if (defaultModel is not null)
            {
                sb.AppendLine($"    public static string {pascalProvider}DefaultModelId => \"{EscapeString(defaultModel.Value)}\";");
            }

            var fastDefaultModel = group.FirstOrDefault(m => m.IsFastDefault);
            if (fastDefaultModel is not null)
            {
                sb.AppendLine($"    public static string {pascalProvider}DefaultFastModelId => \"{EscapeString(fastDefaultModel.Value)}\";");
            }

            sb.AppendLine();
        }

        sb.AppendLine("}");

        context.AddSource($"{enumInfo.Name}ModelEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private sealed class EnumInfo
    {
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public string Namespace { get; }
        public ImmutableArray<EnumMemberInfo> Members { get; }
        public ImmutableArray<ModelMemberInfo> ModelMembers { get; }

        public EnumInfo(string fullyQualifiedName, string name, string ns, ImmutableArray<EnumMemberInfo> members, ImmutableArray<ModelMemberInfo> modelMembers)
        {
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            Namespace = ns;
            Members = members;
            ModelMembers = modelMembers;
        }
    }

    private sealed class EnumMemberInfo
    {
        public string Name { get; }
        public string Value { get; }
        public ImmutableArray<string> Aliases { get; }

        public EnumMemberInfo(string name, string value, ImmutableArray<string> aliases)
        {
            Name = name;
            Value = value;
            Aliases = aliases;
        }
    }

    private sealed class ModelMemberInfo
    {
        public string Name { get; }
        public string Value { get; }
        public string Provider { get; }
        public string DisplayName { get; }
        public int ContextWindow { get; }
        public string Description { get; }
        public bool IsDefault { get; }
        public bool IsFastDefault { get; }

        public ModelMemberInfo(string name, string value, string provider, string displayName, int contextWindow, string description, bool isDefault, bool isFastDefault)
        {
            Name = name;
            Value = value;
            Provider = provider;
            DisplayName = displayName;
            ContextWindow = contextWindow;
            Description = description;
            IsDefault = isDefault;
            IsFastDefault = isFastDefault;
        }
    }
}
