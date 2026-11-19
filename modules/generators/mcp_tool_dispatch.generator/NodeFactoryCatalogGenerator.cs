
namespace McpToolDispatch.Generator;

/// <summary>
/// 节点工厂目录生成器 — 扫描 <c>[NodeFactory]</c> 标注的类型，产出**静态**的节点工厂清单。
/// <para>
/// TASK100：节点工厂此前集中写在 <c>Pipelines</c> 的 <c>BuildNodeFactories()</c> 里，
/// 而 <c>Pipelines</c> 的编译闭包并不覆盖全部实现域，导致部分域天生无法成为节点。
/// 改为「域内标注 → 生成器静态聚合」后，装配层只汇总、不认识具体类型。
/// </para>
/// <para>
/// 为什么是静态清单而非 DI/反射：AOT 是第一约束（TASK097 G7 已验证），
/// 且 <c>--dump-nodes</c> 诊断命令不该为了拿一份键列表去建真实宿主容器。
/// </para>
/// <para>
/// 扫描范围与 <see cref="ServiceRegistrationGenerator"/> 保持一致：
/// Exe 与显式声明 <c>AutoRegisterScope=CurrentAssembly</c> 的聚合库只看自身程序集，其余库看完整闭包。
/// 因此「最宽闭包」的程序集（Composition）拿到的是全量清单。
/// </para>
/// </summary>
[Generator]
public sealed class NodeFactoryCatalogGenerator : IIncrementalGenerator {
    private const string NodeFactoryAttributeFullName = "JoinCode.Abstractions.Attributes.NodeFactoryAttribute";

    private static readonly DiagnosticDescriptor DuplicateKeyRule = new(
        "JCC4013",
        "节点工厂键重复",
        "节点键 '{0}' 被重复声明（{1}）。同一 consumer:nodeId 只能有一个工厂，否则装配顺序不确定。请删除重复的 [NodeFactory]。",
        "NodeFactoryCatalog",
        DiagnosticSeverity.Error,
        true,
        "节点键是 profile 声明与实现之间的唯一关联点. 重复会导致 profile 声明指向不确定的实现.");

    private static readonly DiagnosticDescriptor DuplicateAcrossAssemblyRule = new(
        "JCC4014",
        "跨程序集节点键重复",
        "节点键 '{0}' 在多个程序集中重复声明（{1}）。同一 consumer:nodeId 只能有一个实现。",
        "NodeFactoryCatalog",
        DiagnosticSeverity.Error,
        true,
        "同一节点键对应两个实现会让声明与实现的门禁失去意义.");

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var catalog = context.CompilationProvider.Select(static (compilation, _) => {
            var attr = compilation.GetTypeByMetadataName(NodeFactoryAttributeFullName);
            if (attr is null)
                return new NodeFactoryContext(BuildResult.Empty);

            var assemblyName = compilation.AssemblyName ?? "CurrentAssembly";
            var isExe = compilation.Options.OutputKind == OutputKind.ConsoleApplication;
            var scanCurrentOnly = isExe || IsCurrentAssemblyScope(compilation);

            var entries = new List<NodeFactoryEntry>();
            VisitNamespaces(compilation.GlobalNamespace, attr, entries, assemblyName, scanCurrentOnly);

            return new NodeFactoryContext(new BuildResult(
                SanitizeAssemblyName(assemblyName),
                entries.ToImmutableArray()));
        });

        context.RegisterSourceOutput(catalog, static (ctx, c) => GenerateCatalog(ctx, c.Result));
    }

    /// <summary>
    /// 与 <c>ServiceRegistrationGenerator.IsCurrentAssemblyScope</c> 同义：
    /// 聚合库声明 <c>AutoRegisterScope=CurrentAssembly</c> 时只看自身，避免重复登记。
    /// </summary>
    private static bool IsCurrentAssemblyScope(Compilation compilation) {
        foreach (var attr in compilation.Assembly.GetAttributes()) {
            if (attr.ConstructorArguments.Length != 2) continue;
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), "System.Reflection.AssemblyMetadataAttribute", StringComparison.Ordinal)) continue;
            if (attr.ConstructorArguments[0].Value is string key && key == "AutoRegisterScope"
                && attr.ConstructorArguments[1].Value is string value && value == "CurrentAssembly")
                return true;
        }
        return false;
    }

    private static string SanitizeAssemblyName(string? assemblyName) {
        if (assemblyName is null || assemblyName.Length == 0)
            return "CurrentAssembly";

        var sb = new StringBuilder(assemblyName.Length);
        var capitalizeNext = false;
        foreach (var ch in assemblyName) {
            if (char.IsLetterOrDigit(ch) || ch == '_') {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            } else {
                capitalizeNext = true;
            }
        }

        var result = sb.ToString();
        if (result.Length == 0)
            return "CurrentAssembly";
        if (char.IsDigit(result[0]))
            result = "A" + result;
        return char.ToUpperInvariant(result[0]) + result.Substring(1);
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol factoryAttr,
        List<NodeFactoryEntry> results,
        string currentAssemblyName,
        bool filterByCurrentAssembly) {
        foreach (var member in namespaceSymbol.GetMembers()) {
            if (member is INamespaceSymbol childNamespace) {
                VisitNamespaces(childNamespace, factoryAttr, results, currentAssemblyName, filterByCurrentAssembly);
                continue;
            }

            if (member is not INamedTypeSymbol typeSymbol)
                continue;

            if (filterByCurrentAssembly
                && !string.Equals(typeSymbol.ContainingAssembly?.Name, currentAssemblyName, StringComparison.Ordinal))
                continue;

            if (typeSymbol.TypeKind == TypeKind.Error)
                continue;

            var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var assembly = typeSymbol.ContainingAssembly?.Name ?? "";

            foreach (var attr in typeSymbol.GetAttributes()) {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, factoryAttr))
                    continue;
                if (attr.ConstructorArguments.Length < 2)
                    continue;

                if (attr.ConstructorArguments[0].Value is not string consumer || string.IsNullOrWhiteSpace(consumer))
                    continue;
                if (attr.ConstructorArguments[1].Value is not string nodeId || string.IsNullOrWhiteSpace(nodeId))
                    continue;

                results.Add(new NodeFactoryEntry(
                    $"{consumer}:{nodeId}",
                    typeName,
                    assembly,
                    typeSymbol.Locations.FirstOrDefault() ?? Location.None));
            }
        }
    }

    private static void GenerateCatalog(SourceProductionContext context, BuildResult result) {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Core.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {result.SanitizedAssemblyName} 程序集可解析到的节点工厂清单（由源码生成器 NodeFactoryCatalogGenerator 生成）。");
        sb.AppendLine("/// 静态清单：无反射、无运行期扫描，AOT 友好。");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("internal static class NodeFactoryCatalog {");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 枚举本编译单元可见的 <c>[NodeFactory]</c> 节点工厂。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <returns>键为 consumer:nodeId 的工厂序列。</returns>");
        sb.AppendLine("    internal static global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, global::System.Func<global::System.IServiceProvider, object>>> Create() {");

        var reportedDuplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        var reportedAcrossAssemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in result.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).ThenBy(e => e.TypeName, StringComparer.Ordinal)) {
            if (!reportedDuplicateKeys.Add(entry.Key)) {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateKeyRule, entry.Location, entry.Key, entry.TypeName));
                continue;
            }

            sb.AppendLine("        yield return new global::System.Collections.Generic.KeyValuePair<string, global::System.Func<global::System.IServiceProvider, object>>(");
            sb.AppendLine($"            \"{entry.Key}\",");
            sb.AppendLine($"            static sp => sp.GetRequiredService<{entry.TypeName}>());");
        }

        // 跨程序集重名：同键不同实现，说明「声明 ↔ 实现」不再一对一（本编译单元内已去重，这里提示人工介入）。
        foreach (var group in result.Entries.GroupBy(e => e.Key, StringComparer.Ordinal)) {
            var assemblies = group.Select(e => e.Assembly).Distinct(StringComparer.Ordinal).ToList();
            if (assemblies.Count <= 1)
                continue;
            if (!reportedAcrossAssemblies.Add(group.Key))
                continue;
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateAcrossAssemblyRule,
                group.First().Location,
                group.Key,
                string.Join(" / ", assemblies)));
        }

        // 即使一条都没有也要留下 yield break，否则生成的方法不是迭代器、缺 return 会 CS0161。
        sb.AppendLine("        yield break;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedNodeFactoryCatalog.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private sealed class BuildResult {
        public static readonly BuildResult Empty = new("CurrentAssembly", ImmutableArray<NodeFactoryEntry>.Empty);

        public string SanitizedAssemblyName { get; }
        public ImmutableArray<NodeFactoryEntry> Entries { get; }

        public BuildResult(string sanitizedAssemblyName, ImmutableArray<NodeFactoryEntry> entries) {
            SanitizedAssemblyName = sanitizedAssemblyName;
            Entries = entries;
        }
    }

    private sealed class NodeFactoryContext {
        public BuildResult Result { get; }

        public NodeFactoryContext(BuildResult result) {
            Result = result;
        }
    }

    private sealed class NodeFactoryEntry {
        public string Key { get; }
        public string TypeName { get; }
        public string Assembly { get; }
        public Location Location { get; }

        public NodeFactoryEntry(string key, string typeName, string assembly, Location location) {
            Key = key;
            TypeName = typeName;
            Assembly = assembly;
            Location = location;
        }
    }
}
