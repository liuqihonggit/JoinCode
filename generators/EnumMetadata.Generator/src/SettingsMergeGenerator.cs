
namespace SettingsMerge.Generator;

/// <summary>
/// Settings 合并源码生成器 — 扫描 [SettingsMerge] 特性，自动生成拷贝构造函数、Merge、GetSettingByKey、UpdateSettingByKey
/// </summary>
[Generator]
public sealed class SettingsMergeGenerator : IIncrementalGenerator
{
    private const string SettingsMergeAttributeFullName = "JoinCode.Abstractions.Attributes.SettingsMergeAttribute";
    private const string SettingsPropertyAttributeFullName = "JoinCode.Abstractions.Attributes.SettingsPropertyAttribute";
    private const string JsonPropertyNameAttributeFullName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classInfos = context.CompilationProvider
            .SelectMany(static (compilation, _) =>
            {
                var mergeAttr = compilation.GetTypeByMetadataName(SettingsMergeAttributeFullName);
                var propAttr = compilation.GetTypeByMetadataName(SettingsPropertyAttributeFullName);
                var jsonPropAttr = compilation.GetTypeByMetadataName(JsonPropertyNameAttributeFullName);
                if (mergeAttr is null || propAttr is null)
                    return ImmutableArray<SettingsClassInfo>.Empty;

                var results = new List<SettingsClassInfo>();
                VisitNamespaces(compilation.GlobalNamespace, compilation.Assembly, mergeAttr, propAttr, jsonPropAttr, results);
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(classInfos, static (ctx, classes) =>
        {
            foreach (var classInfo in classes)
            {
                GenerateSettingsMergeCode(ctx, classInfo);
            }
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        IAssemblySymbol currentAssembly,
        INamedTypeSymbol mergeAttr,
        INamedTypeSymbol propAttr,
        INamedTypeSymbol? jsonPropAttr,
        List<SettingsClassInfo> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, currentAssembly, mergeAttr, propAttr, jsonPropAttr, results);
            else if (member is INamedTypeSymbol { TypeKind: TypeKind.Class } classType
                     && SymbolEqualityComparer.Default.Equals(classType.ContainingAssembly, currentAssembly))
            {
                var hasMergeAttr = classType.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, mergeAttr));
                if (!hasMergeAttr) continue;

                var properties = new List<SettingsPropertyInfo>();
                foreach (var prop in classType.GetMembers().OfType<IPropertySymbol>())
                {
                    if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                    if (prop.SetMethod is null) continue;

                    var propAttrData = prop.GetAttributes()
                        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, propAttr));

                    var jsonName = jsonPropAttr is not null
                        ? prop.GetAttributes()
                            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, jsonPropAttr))
                            ?.ConstructorArguments.ElementAtOrDefault(0).Value as string
                        : null;

                    var strategy = SettingsMergeStrategy.Override;
                    bool skipCopy = false, skipMerge = false, skipKeyAccess = false;
                    string? dictValueType = null;
                    string? customMergeMethod = null;

                    if (propAttrData is not null)
                    {
                        strategy = (SettingsMergeStrategy)(propAttrData.ConstructorArguments.ElementAtOrDefault(0).Value as int? ?? 0);
                        foreach (var named in propAttrData.NamedArguments)
                        {
                            switch (named.Key)
                            {
                                case "SkipCopy": skipCopy = (bool)(named.Value.Value ?? false); break;
                                case "SkipMerge": skipMerge = (bool)(named.Value.Value ?? false); break;
                                case "SkipKeyAccess": skipKeyAccess = (bool)(named.Value.Value ?? false); break;
                                case "DictionaryValueType": dictValueType = named.Value.Value as string; break;
                                case "CustomMergeMethod": customMergeMethod = named.Value.Value as string; break;
                            }
                        }
                    }

                    properties.Add(new SettingsPropertyInfo(
                        prop.Name,
                        prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        prop.Type.NullableAnnotation == NullableAnnotation.Annotated,
                        jsonName,
                        strategy,
                        skipCopy,
                        skipMerge,
                        skipKeyAccess,
                        dictValueType,
                        customMergeMethod));
                }

                results.Add(new SettingsClassInfo(
                    classType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    classType.Name,
                    classType.ContainingNamespace.ToDisplayString(),
                    properties.ToImmutableArray()));
            }
        }
    }

    private static void GenerateSettingsMergeCode(SourceProductionContext context, SettingsClassInfo classInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {classInfo.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {classInfo.Name}");
        sb.AppendLine("{");

        // 1. 拷贝构造函数
        GenerateCopyConstructor(sb, classInfo);

        // 2. Merge 方法
        GenerateMergeMethod(sb, classInfo);

        // 3. GetSettingByKey 方法
        GenerateGetSettingByKey(sb, classInfo);

        // 4. UpdateSettingByKey 方法
        GenerateUpdateSettingByKey(sb, classInfo);

        sb.AppendLine("}");

        context.AddSource($"{classInfo.Name}Merge.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateCopyConstructor(StringBuilder sb, SettingsClassInfo classInfo)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 拷贝构造函数 — 自动生成");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public {classInfo.Name}({classInfo.FullyQualifiedName} other)");
        sb.AppendLine("    {");

        foreach (var prop in classInfo.Properties)
        {
            if (prop.SkipCopy) continue;

            var propName = prop.Name;
            var propType = prop.FullyQualifiedType;

            if (prop.Strategy == SettingsMergeStrategy.DictionaryMerge && prop.DictValueType is not null)
            {
                // Dictionary 深拷贝
                sb.AppendLine($"        {propName} = other.{propName} is not null ? new Dictionary<string, {prop.DictValueType}>(other.{propName}, StringComparer.OrdinalIgnoreCase) : null;");
            }
            else
            {
                // 简单赋值
                sb.AppendLine($"        {propName} = other.{propName};");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateMergeMethod(StringBuilder sb, SettingsClassInfo classInfo)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 合并两个 Settings（低优先级 + 高优先级）— 自动生成");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static {classInfo.FullyQualifiedName} Merge({classInfo.FullyQualifiedName}? baseSettings, {classInfo.FullyQualifiedName}? overrideSettings)");
        sb.AppendLine("    {");
        sb.AppendLine($"        if (baseSettings is null) return overrideSettings ?? new {classInfo.FullyQualifiedName}();");
        sb.AppendLine($"        if (overrideSettings is null) return baseSettings;");
        sb.AppendLine();
        sb.AppendLine($"        return new {classInfo.FullyQualifiedName}");
        sb.AppendLine("        {");

        foreach (var prop in classInfo.Properties)
        {
            if (prop.SkipMerge) continue;

            var propName = prop.Name;

            switch (prop.Strategy)
            {
                case SettingsMergeStrategy.Override:
                    sb.AppendLine($"            {propName} = overrideSettings.{propName} ?? baseSettings.{propName},");
                    break;

                case SettingsMergeStrategy.DictionaryMerge:
                    if (prop.CustomMergeMethod is not null)
                    {
                        sb.AppendLine($"            {propName} = {prop.CustomMergeMethod}(baseSettings.{propName}, overrideSettings.{propName}),");
                    }
                    else
                    {
                        sb.AppendLine($"            {propName} = MergeDictionaries(baseSettings.{propName}, overrideSettings.{propName}),");
                    }
                    break;

                case SettingsMergeStrategy.ListConcatDistinct:
                    sb.AppendLine($"            {propName} = MergeLists(baseSettings.{propName}, overrideSettings.{propName}),");
                    break;

                case SettingsMergeStrategy.RecursiveMerge:
                    if (prop.CustomMergeMethod is not null)
                    {
                        sb.AppendLine($"            {propName} = {prop.CustomMergeMethod}(baseSettings.{propName}, overrideSettings.{propName}),");
                    }
                    else
                    {
                        sb.AppendLine($"            {propName} = overrideSettings.{propName} ?? baseSettings.{propName},");
                    }
                    break;

                case SettingsMergeStrategy.Custom:
                    if (prop.CustomMergeMethod is not null)
                    {
                        sb.AppendLine($"            {propName} = {prop.CustomMergeMethod}(baseSettings.{propName}, overrideSettings.{propName}),");
                    }
                    break;
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 生成辅助方法
        GenerateMergeHelpers(sb, classInfo);
    }

    private static void GenerateMergeHelpers(StringBuilder sb, SettingsClassInfo classInfo)
    {
        var needsDictMerge = classInfo.Properties.Any(p => p.Strategy == SettingsMergeStrategy.DictionaryMerge && p.CustomMergeMethod is null);
        var needsListMerge = classInfo.Properties.Any(p => p.Strategy == SettingsMergeStrategy.ListConcatDistinct);

        if (needsDictMerge)
        {
            // Dictionary<string, string> 版本
            sb.AppendLine("    private static Dictionary<string, string>? MergeDictionaries(Dictionary<string, string>? baseDict, Dictionary<string, string>? overrideDict)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (baseDict is null && overrideDict is null) return null;");
            sb.AppendLine("        if (baseDict is null) return overrideDict;");
            sb.AppendLine("        if (overrideDict is null) return baseDict;");
            sb.AppendLine();
            sb.AppendLine("        var result = new Dictionary<string, string>(baseDict, StringComparer.OrdinalIgnoreCase);");
            sb.AppendLine("        foreach (var (key, value) in overrideDict)");
            sb.AppendLine("            result[key] = value;");
            sb.AppendLine();
            sb.AppendLine("        return result;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Dictionary<string, T> 泛型版本
            var dictProps = classInfo.Properties
                .Where(p => p.Strategy == SettingsMergeStrategy.DictionaryMerge && p.CustomMergeMethod is null && p.DictValueType is not null && p.DictValueType != "string")
                .GroupBy(p => p.DictValueType)
                .ToList();

            foreach (var group in dictProps)
            {
                var valueType = group.Key;
                sb.AppendLine($"    private static Dictionary<string, {valueType}>? MergeDictionaries(Dictionary<string, {valueType}>? baseDict, Dictionary<string, {valueType}>? overrideDict)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (baseDict is null && overrideDict is null) return null;");
                sb.AppendLine("        if (baseDict is null) return overrideDict;");
                sb.AppendLine("        if (overrideDict is null) return baseDict;");
                sb.AppendLine();
                sb.AppendLine($"        var result = new Dictionary<string, {valueType}>(baseDict, StringComparer.OrdinalIgnoreCase);");
                sb.AppendLine("        foreach (var (key, value) in overrideDict)");
                sb.AppendLine("            result[key] = value;");
                sb.AppendLine();
                sb.AppendLine("        return result;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        if (needsListMerge)
        {
            sb.AppendLine("    private static List<string>? MergeLists(List<string>? baseList, List<string>? overrideList)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (baseList is null && overrideList is null) return null;");
            sb.AppendLine("        if (baseList is null) return overrideList;");
            sb.AppendLine("        if (overrideList is null) return baseList;");
            sb.AppendLine();
            sb.AppendLine("        return baseList.Concat(overrideList).Distinct(StringComparer.OrdinalIgnoreCase).ToList();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    private static void GenerateGetSettingByKey(StringBuilder sb, SettingsClassInfo classInfo)
    {
        var keyProps = classInfo.Properties.Where(p => !p.SkipKeyAccess && p.JsonName is not null).ToList();
        if (keyProps.Count == 0) return;

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 从强类型 Settings 中按键名获取值 — 自动生成");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public string? GetSettingByKey(string key)");
        sb.AppendLine("    {");
        sb.AppendLine("        return key switch");
        sb.AppendLine("        {");

        foreach (var prop in keyProps)
        {
            // 只有 string? 类型的属性可以直接返回
            if (prop.FullyQualifiedType == "string?" || prop.FullyQualifiedType == "string")
            {
                sb.AppendLine($"            \"{prop.JsonName}\" => {prop.Name},");
            }
        }

        sb.AppendLine("            _ => null,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateUpdateSettingByKey(StringBuilder sb, SettingsClassInfo classInfo)
    {
        var keyProps = classInfo.Properties.Where(p => !p.SkipKeyAccess && p.JsonName is not null).ToList();
        if (keyProps.Count == 0) return;

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 更新指定键的值，返回新对象（不可变）— 自动生成");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public {classInfo.FullyQualifiedName} UpdateSettingByKey(string key, string? value)");
        sb.AppendLine("    {");
        sb.AppendLine("        return key switch");
        sb.AppendLine("        {");

        foreach (var prop in keyProps)
        {
            var propName = prop.Name;
            var jsonName = prop.JsonName;
            var propType = prop.FullyQualifiedType;

            if (propType == "string?" || propType == "string")
            {
                sb.AppendLine($"            \"{jsonName}\" => new {classInfo.FullyQualifiedName}(this) {{ {propName} = value }},");
            }
            else if (propType == "bool?" || propType == "bool")
            {
                sb.AppendLine($"            \"{jsonName}\" => new {classInfo.FullyQualifiedName}(this) {{ {propName} = bool.TryParse(value, out var b) ? b : (bool?)null }},");
            }
            else if (propType == "int?" || propType == "int")
            {
                sb.AppendLine($"            \"{jsonName}\" => new {classInfo.FullyQualifiedName}(this) {{ {propName} = int.TryParse(value, out var i) ? i : (int?)null }},");
            }
        }

        sb.AppendLine($"            _ => new {classInfo.FullyQualifiedName}(this),");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private sealed class SettingsClassInfo
    {
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public string Namespace { get; }
        public ImmutableArray<SettingsPropertyInfo> Properties { get; }

        public SettingsClassInfo(string fullyQualifiedName, string name, string ns, ImmutableArray<SettingsPropertyInfo> properties)
        {
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            Namespace = ns;
            Properties = properties;
        }
    }

    private sealed class SettingsPropertyInfo
    {
        public string Name { get; }
        public string FullyQualifiedType { get; }
        public bool IsNullable { get; }
        public string? JsonName { get; }
        public SettingsMergeStrategy Strategy { get; }
        public bool SkipCopy { get; }
        public bool SkipMerge { get; }
        public bool SkipKeyAccess { get; }
        public string? DictValueType { get; }
        public string? CustomMergeMethod { get; }

        public SettingsPropertyInfo(
            string name,
            string fullyQualifiedType,
            bool isNullable,
            string? jsonName,
            SettingsMergeStrategy strategy,
            bool skipCopy,
            bool skipMerge,
            bool skipKeyAccess,
            string? dictValueType,
            string? customMergeMethod)
        {
            Name = name;
            FullyQualifiedType = fullyQualifiedType;
            IsNullable = isNullable;
            JsonName = jsonName;
            Strategy = strategy;
            SkipCopy = skipCopy;
            SkipMerge = skipMerge;
            SkipKeyAccess = skipKeyAccess;
            DictValueType = dictValueType;
            CustomMergeMethod = customMergeMethod;
        }
    }

    private enum SettingsMergeStrategy
    {
        Override = 0,
        DictionaryMerge = 1,
        ListConcatDistinct = 2,
        RecursiveMerge = 3,
        Custom = 4,
    }
}
