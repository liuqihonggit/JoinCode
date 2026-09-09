
namespace EnumMetadata.Generator;

/// <summary>
/// 枚举元数据源码生成器 — 扫描 [EnumValue] 特性，自动生成 ToValue/FromValue 映射代码
/// </summary>
[Generator]
public sealed class EnumMetadataGenerator : IIncrementalGenerator
{
    private const string EnumValueAttributeFullName = "JoinCode.Abstractions.Attributes.EnumValueAttribute";
    private const string AliasValueAttributeFullName = "JoinCode.Abstractions.Attributes.AliasValueAttribute";
    private const string SubCommandInfoAttributeFullName = "JoinCode.Abstractions.Attributes.SubCommandInfoAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enumInfos = context.CompilationProvider
            .SelectMany(static (compilation, _) =>
            {
                var enumValueAttr = compilation.GetTypeByMetadataName(EnumValueAttributeFullName);
                var aliasValueAttr = compilation.GetTypeByMetadataName(AliasValueAttributeFullName);
                var subCommandInfoAttr = compilation.GetTypeByMetadataName(SubCommandInfoAttributeFullName);
                if (enumValueAttr is null)
                    return ImmutableArray<EnumInfo>.Empty;

                var results = new List<EnumInfo>();
                VisitNamespaces(compilation.GlobalNamespace, compilation.Assembly, enumValueAttr, aliasValueAttr, subCommandInfoAttr, results);
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(enumInfos, static (ctx, enums) =>
        {
            foreach (var enumInfo in enums)
            {
                GenerateExtensionClass(ctx, enumInfo);
            }
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol currentAssembly,
        INamedTypeSymbol enumValueAttr,
        INamedTypeSymbol? aliasValueAttr,
        INamedTypeSymbol? subCommandInfoAttr,
        List<EnumInfo> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, currentAssembly, enumValueAttr, aliasValueAttr, subCommandInfoAttr, results);
            else if (member is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                     && SymbolEqualityComparer.Default.Equals(enumType.ContainingAssembly, currentAssembly))
            {
                var members = new List<EnumMemberInfo>();
                foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    var allAttrs = field.GetAttributes()
                        .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, enumValueAttr))
                        .ToList();

                    if (allAttrs.Count > 0)
                    {
                        var value = allAttrs[0].ConstructorArguments.ElementAtOrDefault(0).Value as string ?? field.Name;

                        var aliases = ImmutableArray<string>.Empty;
                        if (allAttrs.Count > 1)
                        {
                            aliases = allAttrs.Skip(1)
                                .Select(a => a.ConstructorArguments.ElementAtOrDefault(0).Value as string)
                                .Where(a => a is not null)
                                .Cast<string>()
                                .ToImmutableArray();
                        }
                        else if (aliasValueAttr is not null)
                        {
                            var aliasAttrs = field.GetAttributes()
                                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, aliasValueAttr))
                                .Select(a => a.ConstructorArguments.ElementAtOrDefault(0).Value as string)
                                .Where(a => a is not null)
                                .Cast<string>()
                                .ToImmutableArray();
                            aliases = aliasAttrs;
                        }

                        SubCommandInfo? subCmdInfo = null;
                        if (subCommandInfoAttr is not null)
                        {
                            var subCmdAttr = field.GetAttributes()
                                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, subCommandInfoAttr));
                            if (subCmdAttr is not null)
                            {
                                var desc = subCmdAttr.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? "";
                                var cat = subCmdAttr.ConstructorArguments.ElementAtOrDefault(1).Value as string ?? "";
                                var example = subCmdAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Example").Value.Value as string;
                                var isAlias = subCmdAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "IsAlias").Value.Value is true;
                                var aliasOf = subCmdAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "AliasOf").Value.Value as string;
                                var isDeprecated = subCmdAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "IsDeprecated").Value.Value is true;
                                subCmdInfo = new SubCommandInfo(desc, cat, example, isAlias, aliasOf, isDeprecated);
                            }
                        }

                        members.Add(new EnumMemberInfo(field.Name, value, aliases, subCmdInfo));
                    }
                }

                if (members.Count > 0)
                {
                    results.Add(new EnumInfo(
                        enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        enumType.Name,
                        enumType.ContainingNamespace.ToDisplayString(),
                        members.ToImmutableArray()));
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

        // 如果枚举有 [SubCommandInfo] 标注, 生成多级渐进式展开帮助文本类
        var subCmdMembers = enumInfo.Members.Where(m => m.SubCommandInfo is not null).ToList();
        if (subCmdMembers.Count > 0)
        {
            GenerateSubCommandHelpText(sb, enumInfo, subCmdMembers);
        }

        context.AddSource($"{enumInfo.Name}Extensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 生成 SubCommandHelpText 类 — 多级渐进式展开帮助文本
    /// </summary>
    private static void GenerateSubCommandHelpText(StringBuilder sb, EnumInfo enumInfo, List<EnumMemberInfo> subCmdMembers)
    {
        sb.AppendLine();
        sb.AppendLine($"public static class {enumInfo.Name}HelpText");
        sb.AppendLine("{");

        // 生成 SubCommandEntry 记录
        sb.AppendLine("    public sealed record SubCommandEntry(string Command, string Description, string Category, string? Example, bool IsAlias, string? AliasOf, bool IsDeprecated);");
        sb.AppendLine();

        // 生成 __allEntries 数组
        sb.AppendLine("    private static readonly SubCommandEntry[] __allEntries = new SubCommandEntry[]");
        sb.AppendLine("    {");
        foreach (var m in subCmdMembers)
        {
            var info = m.SubCommandInfo!;
            sb.AppendLine($"        new(\"{EscapeString(m.Value)}\", \"{EscapeString(info.Description)}\", \"{EscapeString(info.Category)}\", {NullableString(info.Example)}, {(info.IsAlias ? "true" : "false")}, {NullableString(info.AliasOf)}, {(info.IsDeprecated ? "true" : "false")}),");
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // 生成 __byCommand 字典
        sb.AppendLine("    private static readonly FrozenDictionary<string, SubCommandEntry> __byCommand = __allEntries.ToFrozenDictionary(e => e.Command, StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine();

        // GetCategories 方法 — 列出所有分类及数量
        sb.AppendLine("    // 列出所有分类及命令数量 — jcc -h 的第一级展开");
        sb.AppendLine("    public static string GetCategories()");
        sb.AppendLine("    {");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(256);");
        sb.AppendLine("        foreach (var cat in __allEntries.Where(e => !e.IsAlias).GroupBy(e => e.Category).OrderBy(g => g.Key))");
        sb.AppendLine("        {");
        sb.AppendLine("            var count = cat.Count(e => !e.IsAlias);");
        sb.AppendLine("            sb.AppendLine($\"  {cat.Key} ({count} 个)\");");
        sb.AppendLine("        }");
        sb.AppendLine("        sb.AppendLine();");
        sb.AppendLine("        sb.Append(\"用 jcc -h <分类> 查看该分类下的命令, 或 jcc -h <命令名> 查看命令详情\");");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByCategory 方法 — 列出某分类下的所有命令
        sb.AppendLine("    // 列出指定分类下的所有命令 — jcc -h <分类> 的第二级展开");
        sb.AppendLine("    public static string GetByCategory(string category)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(512);");
        sb.AppendLine("        var entries = __allEntries.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase) && !e.IsAlias).ToList();");
        sb.AppendLine("        if (entries.Count == 0) return $\"未知分类: {category}\\n\\n可用分类:\\n{GetCategories()}\";");
        sb.AppendLine("        sb.AppendLine($\"{category}:\");");
        sb.AppendLine("        sb.AppendLine();");
        sb.AppendLine("        foreach (var e in entries)");
        sb.AppendLine("        {");
        sb.AppendLine("            var dep = e.IsDeprecated ? \" [已废弃]\" : \"\";");
        sb.AppendLine("            sb.AppendLine($\"  {e.Command,-30} {e.Description}{dep}\");");
        sb.AppendLine("        }");
        sb.AppendLine("        sb.AppendLine();");
        sb.AppendLine("        sb.Append(\"用 jcc -h <命令名> 查看命令详情\");");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByCommand 方法 — 显示某命令的详细帮助
        sb.AppendLine("    // 显示指定命令的详细帮助 — jcc -h <命令名> 的第二级展开");
        sb.AppendLine("    public static string GetByCommand(string command)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!__byCommand.TryGetValue(command, out var entry))");
        sb.AppendLine("            return $\"未知命令: {command}\\n\\n可用分类:\\n{GetCategories()}\";");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(256);");
        sb.AppendLine("        sb.AppendLine($\"{entry.Command} — {entry.Description}\");");
        sb.AppendLine("        if (entry.IsDeprecated) sb.AppendLine(\"[已废弃]\");");
        sb.AppendLine("        if (entry.IsAlias && entry.AliasOf is not null) sb.AppendLine($\"别名 of: {entry.AliasOf}\");");
        sb.AppendLine("        if (entry.Example is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            sb.AppendLine();");
        sb.AppendLine("            sb.AppendLine(\"示例:\");");
        sb.AppendLine("            sb.AppendLine($\"  {entry.Example}\");");
        sb.AppendLine("        }");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetHelp 方法 — 统一入口, 按 topic 分发
        sb.AppendLine("    // 统一帮助入口 — 按 topic 分发到不同级别");
        sb.AppendLine("    // null/empty -> 分类概览, 分类名 -> 该分类命令列表, 命令名 -> 命令详情");
        sb.AppendLine("    public static string GetHelp(string? topic = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(topic)) return GetCategories();");
        sb.AppendLine("        var topicTrimmed = topic.Trim();");
        sb.AppendLine("        if (__allEntries.Any(e => string.Equals(e.Category, topicTrimmed, StringComparison.OrdinalIgnoreCase)))");
        sb.AppendLine("            return GetByCategory(topicTrimmed);");
        sb.AppendLine("        return GetByCommand(topicTrimmed);");
        sb.AppendLine("    }");

        sb.AppendLine("}");
    }

    private static string NullableString(string? s) => s is null ? "null" : $"\"{EscapeString(s)}\"";

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

    private sealed class EnumInfo
    {
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public string Namespace { get; }
        public ImmutableArray<EnumMemberInfo> Members { get; }

        public EnumInfo(string fullyQualifiedName, string name, string ns, ImmutableArray<EnumMemberInfo> members)
        {
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            Namespace = ns;
            Members = members;
        }
    }

    private sealed class EnumMemberInfo
    {
        public string Name { get; }
        public string Value { get; }
        public ImmutableArray<string> Aliases { get; }
        public SubCommandInfo? SubCommandInfo { get; }

        public EnumMemberInfo(string name, string value, ImmutableArray<string> aliases, SubCommandInfo? subCommandInfo = null)
        {
            Name = name;
            Value = value;
            Aliases = aliases;
            SubCommandInfo = subCommandInfo;
        }
    }

    private sealed class SubCommandInfo
    {
        public string Description { get; }
        public string Category { get; }
        public string? Example { get; }
        public bool IsAlias { get; }
        public string? AliasOf { get; }
        public bool IsDeprecated { get; }

        public SubCommandInfo(string description, string category, string? example, bool isAlias, string? aliasOf, bool isDeprecated)
        {
            Description = description;
            Category = category;
            Example = example;
            IsAlias = isAlias;
            AliasOf = aliasOf;
            IsDeprecated = isDeprecated;
        }
    }
}
