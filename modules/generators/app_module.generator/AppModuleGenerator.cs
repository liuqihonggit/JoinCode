



namespace AppModule.Generator;

/// <summary>
/// AppModule 源码生成器 — 扫描 [AppModule] 只负责「发现模块」，
/// 启动顺序与会话分组由 resources/app/modules.yaml 的 profiles 显式声明。
/// <para>
/// 每个 profile 声明自己归属的程序集，生成器只校验并生成「归属本程序集」的那一份：
/// CLI（程序集 jcc）与引擎（程序集 JoinCode.Engine）各有一份，CLI 引用引擎，
/// 于是同一个编译里既有本程序集的模块、也有引用来的模块 —— 不声明归属就分不清
/// 「这个名字属于另一个程序集」与「这个名字打错了」。
/// </para>
/// </summary>
[Generator]
public sealed class AppModuleGenerator : IIncrementalGenerator {
    private const string AppModuleAttributeFullName = "JoinCode.App.Builder.AppModuleAttribute";

    /// <summary>YAML 与特性之间不一致 — 显式设计不允许静默漏掉或多出一个模块。</summary>
    private static readonly DiagnosticDescriptor ModuleMismatch = new(
        "JCCGEN002",
        "模块注册名不一致",
        "模块装配表与 [AppModule] 特性之间不一致（{0}）",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // 程序集名与模块类型分开取（与 McpToolDispatchGenerator 同一惯用写法）：
        // 前者决定「哪些 profile 归本程序集管」，后者提供 profile 里的注册名。
        var moduleTypes = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "Unknown")
            .Combine(context.CompilationProvider
                .SelectMany(static (compilation, _) => {
                    var appModuleAttr = compilation.GetTypeByMetadataName(AppModuleAttributeFullName);
                    if (appModuleAttr is null)
                        return ImmutableArray<TypeInfo>.Empty;

                    var results = new List<TypeInfo>();
                    VisitNamespaces(compilation.GlobalNamespace, appModuleAttr, compilation, results);
                    return results.ToImmutableArray();
                })
                .Collect());

        var orderFiles = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), "modules.yaml", StringComparison.OrdinalIgnoreCase))
            .Collect();

        context.RegisterSourceOutput(moduleTypes.Combine(orderFiles), static (ctx, pair) => {
            var assemblyName = pair.Left.Left;
            var types = pair.Left.Right;
            var orderSource = pair.Right.Length > 0 ? pair.Right[0].GetText() : null;
            var orderText = orderSource is null ? "" : orderSource.ToString();
            var profiles = OrderYaml.ParseProfiles(orderText);
            if (profiles.Count == 0) {
                ctx.ReportDiagnostic(Diagnostic.Create(ModuleMismatch, Location.None,
                    "resources/app/modules.yaml 没有定义任何 profile"));
                return;
            }

            var owned = profiles
                .Where(entry => string.Equals(entry.Value.Assembly, assemblyName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (owned.Length == 0) {
                ctx.ReportDiagnostic(Diagnostic.Create(ModuleMismatch, Location.None,
                    $"resources/app/modules.yaml 里没有归属本程序集（{assemblyName}）的 profile。"
                    + $"现有 profile 及其归属：{string.Join("、", profiles.Select(entry => $"{entry.Key}→{entry.Value.Assembly}"))}。"
                    + "新增宿主时必须在 YAML 里补一条 assembly 与本程序集 AssemblyName 一致的 profile。"));
                return;
            }

            var byName = types.ToDictionary(
                type => type.ShortName,
                type => type,
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in owned) {
                foreach (var name in entry.Value.Modules) {
                    if (!byName.ContainsKey(name)) {
                        ctx.ReportDiagnostic(Diagnostic.Create(ModuleMismatch, Location.None,
                            $"profile '{entry.Key}' 登记的 '{name}' 不是本程序集（{assemblyName}）能解析的模块类型。"
                            + "模块名拼错时必须报错，不能当成「归另一个程序集管」而静默漏掉；"
                            + "若是引用程序集里的模块，请确认它对本程序集可见（public）。"));
                    }
                }
                foreach (var type in types.Where(type => type.IsLocal)) {
                    if (!entry.Value.Modules.Contains(type.ShortName, StringComparer.OrdinalIgnoreCase)) {
                        ctx.ReportDiagnostic(Diagnostic.Create(ModuleMismatch, Location.None,
                            $"模块 '{type.ShortName}' 是本程序集（{assemblyName}）的模块，"
                            + $"但没有登记到 profile '{entry.Key}'。"
                            + "新增模块必须在这里登记，否则它在启动链里的位置无从确定。"));
                    }
                }
            }

            GenerateModuleCatalog(ctx, owned, byName, assemblyName);
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol targetAttr,
        Compilation compilation,
        List<TypeInfo> results) {
        foreach (var member in namespaceSymbol.GetMembers()) {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, targetAttr, compilation, results);
            else if (member is INamedTypeSymbol typeSymbol) {
                var attr = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, targetAttr));

                if (attr is not null) {
                    var isLocal = SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, compilation.Assembly);
                    results.Add(new TypeInfo(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), isLocal));
                }
            }
        }
    }

    private static void GenerateModuleCatalog(
        SourceProductionContext context,
        IReadOnlyList<KeyValuePair<string, ModuleProfile>> owned,
        IReadOnlyDictionary<string, TypeInfo> byName,
        string assemblyName) {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using JoinCode.App.Builder;");
        sb.AppendLine();
        sb.AppendLine("namespace JoinCode.App.Builder;");
        sb.AppendLine();

        // 每个 profile 一个类型：profile 名从运行时字符串变成编译期类型。
        // 两套装配（CLI / GUI）分属不同程序集，若都生成同名类型，被同时引用的程序集
        // 会撞类型（CS0436）；按 profile 命名后类型名天然唯一。
        foreach (var entry in owned) {
            var typeName = ToPascalCase(entry.Key) + "ModuleCatalog";
            var modules = entry.Value.Modules
                .Where(byName.ContainsKey)
                .Select(name => $"new {byName[name].FullyQualifiedName}()");

            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// {entry.Key} profile 的会话模块目录 — 由 AppModule.Generator 从 resources/app/modules.yaml 生成。");
            sb.AppendLine("/// 顺序与分组都是显式设计，不再扫描特性上的排序数字。");
            sb.AppendLine($"/// 归属程序集：{assemblyName}。");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static class {typeName} {{");
            sb.AppendLine("    /// <summary>模块实例（顺序即 YAML 声明顺序）。</summary>");
            sb.AppendLine($"    public static IEnumerable<IAppModule> Create() => new IAppModule[] {{ {string.Join(", ", modules)} }};");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        context.AddSource("GeneratedAppModuleCatalog.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>profile 名转 PascalCase —— "cli" → "Cli"，生成类型名 <c>CliModuleCatalog</c>。</summary>
    private static string ToPascalCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private sealed class TypeInfo {
        public TypeInfo(string fullyQualifiedName, bool isLocal) {
            FullyQualifiedName = fullyQualifiedName;
            IsLocal = isLocal;
        }

        public string FullyQualifiedName { get; }

        /// <summary>类型是否定义在本程序集（引用程序集里的模块不算）。</summary>
        public bool IsLocal { get; }

        public string ShortName => FullyQualifiedName.Split('.').Last().TrimEnd('>');
    }
}
