
namespace McpToolDispatch.Generator;

[Generator]
public sealed class ServiceRegistrationGenerator : IIncrementalGenerator
{
    private const string RegisterAttributeFullName = "JoinCode.Abstractions.Attributes.RegisterAttribute";
    private const string RegisterOptionsAttributeFullName = "JoinCode.Abstractions.Attributes.RegisterOptionsAttribute";
    private const string AllowCycleAttributeFullName = "JoinCode.Abstractions.Attributes.AllowCycleAttribute";
    private const string IHostedServiceFullName = "global::Microsoft.Extensions.Hosting.IHostedService";

    private static readonly DiagnosticDescriptor DiCycleRule = new(
        "JCC4011",
        "DI 循环依赖",
        "检测到 DI 循环依赖: {0}。请用延迟解析打破循环，或用 [AllowCycle(\"原因\")] 标记已知合法循环。",
        "DIServiceRegistration",
        DiagnosticSeverity.Error,
        true,
        "DI 循环依赖会导致运行期容器解析时栈溢出或死锁. 编译期检测可在开发阶段立即发现问题.");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // === Services ===
        // 注意: Select 返回 IncrementalValueProvider<T>（单值），直接 RegisterSourceOutput 即可，无需 Collect()
        var serviceTypes = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var registerAttr = compilation.GetTypeByMetadataName(RegisterAttributeFullName);
                var allowCycleAttr = compilation.GetTypeByMetadataName(AllowCycleAttributeFullName);
                var assemblyName = compilation.AssemblyName ?? "CurrentAssembly";
                var sanitized = SanitizeAssemblyName(assemblyName);
                var isExe = compilation.Options.OutputKind == OutputKind.ConsoleApplication;

                var results = new List<ServiceRegistrationInfo>();
                if (registerAttr is not null)
                    VisitNamespaces(compilation.GlobalNamespace, registerAttr, allowCycleAttr, results, assemblyName, isExe);

                return new ServiceGenerationContext(sanitized, results.ToImmutableArray());
            });

        context.RegisterSourceOutput(serviceTypes, static (ctx, c) =>
        {
            GenerateRegistrationCode(ctx, c.SanitizedAssemblyName, c.Services);
        });

        // === Options ===
        var optionsRegistrations = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var optionsAttr = compilation.GetTypeByMetadataName(RegisterOptionsAttributeFullName);
                var assemblyName = compilation.AssemblyName ?? "CurrentAssembly";
                var sanitized = SanitizeAssemblyName(assemblyName);
                var isExe = compilation.Options.OutputKind == OutputKind.ConsoleApplication;

                var results = new List<OptionsRegistration>();
                if (optionsAttr is not null)
                    VisitNamespacesForOptions(compilation.GlobalNamespace, optionsAttr, results, assemblyName, isExe);

                return new OptionsGenerationContext(sanitized, results.ToImmutableArray());
            });

        context.RegisterSourceOutput(optionsRegistrations, static (ctx, c) =>
        {
            GenerateOptionsRegistrationCode(ctx, c.SanitizedAssemblyName, c.Registrations);
        });
    }

    /// <summary>
    /// 将程序集名称清理为合法的 C# 标识符片段（用于拼接方法名）。
    /// 例: "JoinCode" → "JoinCode"；"My.Lib" → "MyLib"；"a-b" → "aB"
    /// </summary>
    private static string SanitizeAssemblyName(string? assemblyName)
    {
        if (assemblyName is null)
            throw new ArgumentNullException(nameof(assemblyName));

        if (string.IsNullOrEmpty(assemblyName))
            return "CurrentAssembly";

        var sb = new StringBuilder(assemblyName.Length);
        var capitalizeNext = false;
        for (var i = 0; i < assemblyName.Length; i++)
        {
            var ch = assemblyName[i];
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else
            {
                capitalizeNext = true;
            }
        }

        var result = sb.ToString();
        if (result.Length == 0)
            return "CurrentAssembly";

        // 首字符不能是数字
        if (char.IsDigit(result[0]))
            result = "A" + result;

        // 首字符统一大写，保证生成的方法名符合 PascalCase 约定
        result = char.ToUpperInvariant(result[0]) + result.Substring(1);

        return result;
    }

    /// <summary>
    /// 判断类型是否属于当前程序集。
    /// </summary>
    private static bool IsFromCurrentAssembly(INamedTypeSymbol typeSymbol, string currentAssemblyName)
    {
        return string.Equals(typeSymbol.ContainingAssembly?.Name, currentAssemblyName, System.StringComparison.Ordinal);
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol? registerAttr,
        INamedTypeSymbol? allowCycleAttr,
        List<ServiceRegistrationInfo> results,
        string currentAssemblyName,
        bool filterByCurrentAssembly)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, registerAttr, allowCycleAttr, results, currentAssemblyName, filterByCurrentAssembly);
            else if (member is INamedTypeSymbol typeSymbol)
            {
                // Exe 项目仅扫描自身程序集的类型，避免与被引用库项目生成的同名方法产生 CS0121 歧义
                // 库项目扫描全部类型（含引用程序集），因为依赖的子系统库未启用生成器
                if (filterByCurrentAssembly && !IsFromCurrentAssembly(typeSymbol, currentAssemblyName))
                    continue;

                var registrations = ExtractRegistrations(typeSymbol, registerAttr);
                if (registrations.Count > 0)
                {
                    var constructorDeps = ExtractConstructorDependencies(typeSymbol);
                    var location = typeSymbol.Locations.FirstOrDefault();
                    var hasAllowCycle = typeSymbol.GetAttributes()
                        .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, allowCycleAttr));
                    results.Add(new ServiceRegistrationInfo(
                        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        registrations,
                        constructorDeps,
                        location,
                        hasAllowCycle));
                }
            }
        }
    }

    /// <summary>
    /// 提取类型的构造函数依赖（用于编译期 DI 循环依赖检测）。
    /// 仅扫描 public/internal 构造函数的参数，跳过 Error 类型。
    /// </summary>
    private static List<ConstructorDependency> ExtractConstructorDependencies(INamedTypeSymbol typeSymbol)
    {
        var deps = new List<ConstructorDependency>();
        foreach (var ctor in typeSymbol.Constructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public &&
                ctor.DeclaredAccessibility != Accessibility.Internal)
                continue;

            foreach (var param in ctor.Parameters)
            {
                if (param.Type.TypeKind == TypeKind.Error)
                    continue;

                var typeName = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isOptional = param.HasExplicitDefaultValue ||
                    param.NullableAnnotation == NullableAnnotation.Annotated;
                deps.Add(new ConstructorDependency(typeName, isOptional));
            }
        }
        return deps;
    }

    private static List<ServiceRegistration> ExtractRegistrations(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol? registerAttr)
    {
        var results = new List<ServiceRegistration>();

        if (registerAttr is null)
            return results;

        // 防御性检查：只保留直接标注在当前类型声明语法节点上的 [Register] 特性
        // 避免同文件多类场景下 Roslyn GetAttributes() 返回其他类的特性
        var typeDeclSpans = typeSymbol.DeclaringSyntaxReferences
            .Select(r => (tree: r.SyntaxTree, span: r.Span))
            .ToList();

        foreach (var attr in typeSymbol.GetAttributes().Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, registerAttr)))
        {
            // 验证 [Register] 的语法位置在当前类型声明的范围内
            if (attr.ApplicationSyntaxReference is { } attrRef)
            {
                var attrSpan = attrRef.Span;
                var attrTree = attrRef.SyntaxTree;
                var belongsToType = typeDeclSpans.Any(t =>
                    t.tree == attrTree && t.span.Contains(attrSpan));
                if (!belongsToType)
                    continue;
            }

            var lifetime = LifetimeSingleton;

            // 从构造函数参数提取 Lifetime
            if (attr.ConstructorArguments.Length >= 2)
            {
                var secondArg = attr.ConstructorArguments.ElementAtOrDefault(1);
                if (secondArg.Value is int ctorLifetimeInt)
                    lifetime = ctorLifetimeInt;
            }
            else if (attr.ConstructorArguments.Length == 1)
            {
                var firstArg = attr.ConstructorArguments.ElementAtOrDefault(0);
                if (firstArg.Value is int singleLifetimeInt && firstArg.Type?.TypeKind == TypeKind.Enum)
                    lifetime = singleLifetimeInt;
            }

            // 命名参数覆盖
            var lifetimeArg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Lifetime").Value;
            if (lifetimeArg.Value is int lifetimeInt)
                lifetime = lifetimeInt;

            // 检查是否显式指定了接口类型
            var explicitTypes = new List<string>();
            foreach (var ctorArg in attr.ConstructorArguments)
            {
                if (ctorArg.Value is INamedTypeSymbol typeSym)
                {
                    var typeName = typeSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    // 排除 ServiceLifetime 枚举值（命名空间包含 ServiceLifetime）
                    if (!typeName.Contains("ServiceLifetime") && !typeName.Contains("JoinCode.Abstractions.Attributes"))
                        explicitTypes.Add(typeName);
                }
            }

            if (explicitTypes.Count > 0)
            {
                // 显式指定：注册指定的接口
                foreach (var iface in explicitTypes)
                    results.Add(new ServiceRegistration(iface, lifetime));
            }
            else
            {
                // 无显式指定：自动发现接口和基类
                var autoTypes = new List<string>();

                // 1. 扫描接口（排除 IDisposable/IAsyncDisposable）
                var interfaces = typeSymbol.AllInterfaces
                    .Where(i => i.Name != "IDisposable" && i.Name != "IAsyncDisposable")
                    .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                autoTypes.AddRange(interfaces);

                // 2. 扫描基类（排除 System.Object）
                var baseType = typeSymbol.BaseType;
                if (baseType != null && baseType.Name != "Object")
                {
                    autoTypes.Add(baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                if (autoTypes.Count > 0)
                {
                    foreach (var typeName in autoTypes)
                        results.Add(new ServiceRegistration(typeName, lifetime));
                }
                else
                {
                    // 无接口也无基类：只注册实现类型自身
                    results.Add(new ServiceRegistration(null, lifetime));
                }
            }
        }

        return results;
    }

    private static void GenerateRegistrationCode(SourceProductionContext context, string sanitizedAssemblyName, ImmutableArray<ServiceRegistrationInfo> services)
    {
        // 编译期 DI 循环依赖检测
        DetectCyclesAndReport(context, services);

        // 按实现类型分组，每个实现类型只注册一次
        var groupedByImpl = services
            .GroupBy(s => s.ImplementationName)
            .Select(g => new
            {
                ImplementationName = g.Key,
                Registrations = g.SelectMany(s => s.Registrations).ToList()
            })
            .OrderBy(g => g.ImplementationName)
            .ToList();

        var hasHostedServices = groupedByImpl.Any(g =>
            g.Registrations.Any(r => r.InterfaceName == IHostedServiceFullName));

        var methodName = $"Add{sanitizedAssemblyName}AutoRegisteredServices";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        if (hasHostedServices)
            sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine();
        sb.AppendLine("namespace Core.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static partial class ServiceRegistration");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IServiceCollection {methodName}(this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var group in groupedByImpl)
        {
            var implementationName = group.ImplementationName;
            var regs = group.Registrations;

            // 过滤掉 IHostedService（单独处理）
            var nonHostedRegs = regs.Where(r => r.InterfaceName != IHostedServiceFullName).ToList();
            var hasHosted = regs.Any(r => r.InterfaceName == IHostedServiceFullName);

            if (hasHosted)
            {
                sb.AppendLine($"        services.AddHostedService<{implementationName}>();");
            }

            // 找到主接口（第一个非实现类型自身的接口）
            var primaryInterface = nonHostedRegs
                .FirstOrDefault(r => r.InterfaceName is not null && r.InterfaceName != implementationName);

            if (primaryInterface is not null)
            {
                var addMethodName = primaryInterface.Lifetime switch
                {
                    LifetimeTransient => "AddTransient",
                    LifetimeScoped => "AddScoped",
                    _ => "AddSingleton"
                };

                // 先注册实现类型自身
                sb.AppendLine($"        services.{addMethodName}<{implementationName}>();");

                // 注册所有接口（转发到实现类型）
                var registeredInterfaces = new HashSet<string>();
                foreach (var reg in nonHostedRegs)
                {
                    if (reg.InterfaceName is null || reg.InterfaceName == implementationName)
                        continue;

                    if (!registeredInterfaces.Add(reg.InterfaceName))
                        continue; // 避免重复注册

                    var regAddMethodName = reg.Lifetime switch
                    {
                        LifetimeTransient => "AddTransient",
                        LifetimeScoped => "AddScoped",
                        _ => "AddSingleton"
                    };

                    var shortName = GetShortTypeName(reg.InterfaceName);
                    sb.AppendLine($"        services.{regAddMethodName}<{reg.InterfaceName}>(sp =>");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            if (System.Environment.GetEnvironmentVariable(\"JCC_DI_TRACE\") == \"1\")");
                    sb.AppendLine($"                System.Console.Error.WriteLine(\"[DI] + {shortName}\");");
                    sb.AppendLine($"            var svc = sp.GetRequiredService<{implementationName}>();");
                    sb.AppendLine($"            if (System.Environment.GetEnvironmentVariable(\"JCC_DI_TRACE\") == \"1\")");
                    sb.AppendLine($"                System.Console.Error.WriteLine(\"[DI] - {shortName}\");");
                    sb.AppendLine("            return svc;");
                    sb.AppendLine("        });");
                }
            }
            else
            {
                // 无接口或接口 == 实现类型：只注册一次
                var lifetime = regs.FirstOrDefault()?.Lifetime ?? LifetimeSingleton;
                var addMethodName = lifetime switch
                {
                    LifetimeTransient => "AddTransient",
                    LifetimeScoped => "AddScoped",
                    _ => "AddSingleton"
                };
                sb.AppendLine($"        services.{addMethodName}<{implementationName}>();");
            }
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedServiceRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 从完整限定类型名中提取短名称（用于 DI 日志输出）
    /// 例如: "global::JoinCode.Abstractions.Interfaces.IQueryService" → "IQueryService"
    /// </summary>
    private static string GetShortTypeName(string fullName)
    {
        if (fullName is null)
            throw new ArgumentNullException(nameof(fullName));

        if (fullName.Length == 0)
            return "Unknown";
        var name = fullName;
        // 去掉 global:: 前缀
        if (name.StartsWith("global::"))
            name = name.Substring("global::".Length);
        // 取最后一个 . 之后的部分
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < name.Length - 1)
            name = name.Substring(lastDot + 1);
        return name;
    }

    /// <summary>
    /// 编译期 DI 循环依赖检测：基于 DFS 三色标记法（白=未访问，灰=正在访问，黑=已完成）。
    /// 构建实现类型间的邻接表（接口依赖映射为实现类型，跳过可选依赖），
    /// 发现灰节点即存在环。环中任一类型标注 [AllowCycle] 则豁免，否则报 Diagnostic。
    /// </summary>
    private static void DetectCyclesAndReport(
        SourceProductionContext context,
        ImmutableArray<ServiceRegistrationInfo> services)
    {
        // 构建接口→实现映射
        var interfaceToImpl = new Dictionary<string, string>();
        foreach (var svc in services)
        {
            foreach (var reg in svc.Registrations)
            {
                if (reg.InterfaceName is not null && reg.InterfaceName != svc.ImplementationName)
                    interfaceToImpl[reg.InterfaceName] = svc.ImplementationName;
            }
        }

        // 所有实现类型集合
        var allImpls = new HashSet<string>(services.Select(s => s.ImplementationName));

        // 构建邻接表：实现类型 → 依赖的实现类型
        var adjacency = new Dictionary<string, List<string>>();
        var locationMap = new Dictionary<string, Location?>();
        var allowCycleSet = new HashSet<string>();

        foreach (var svc in services)
        {
            var implName = svc.ImplementationName;
            if (!adjacency.ContainsKey(implName))
                adjacency[implName] = new List<string>();
            locationMap[implName] = svc.Location;
            if (svc.HasAllowCycle)
                allowCycleSet.Add(implName);

            foreach (var dep in svc.ConstructorDependencies)
            {
                if (dep.IsOptional)
                    continue;

                // 将接口依赖替换为实现类型
                var depImpl = dep.DependencyType;
                if (interfaceToImpl.TryGetValue(dep.DependencyType, out var impl))
                    depImpl = impl;

                // 只关注DI服务之间的依赖
                if (allImpls.Contains(depImpl))
                    adjacency[implName].Add(depImpl);
            }
        }

        // DFS三色标记检测环
        var color = new Dictionary<string, int>();
        foreach (var node in adjacency.Keys)
            color[node] = 0;

        var reportedCycles = new HashSet<string>();
        foreach (var node in adjacency.Keys)
        {
            int nodeColor;
            color.TryGetValue(node, out nodeColor);
            if (nodeColor == 0)
            {
                var path = new List<string>();
                DfsCycle(node, adjacency, color, path, context, locationMap, allowCycleSet, reportedCycles);
            }
        }
    }

    /// <summary>
    /// DFS 三色标记递归：0=白未访问，1=灰正在访问，2=黑已完成。
    /// 遇到灰节点表示发现环，提取环路径并检查 [AllowCycle] 豁免。
    /// </summary>
    private static void DfsCycle(
        string node,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, int> color,
        List<string> path,
        SourceProductionContext context,
        Dictionary<string, Location?> locationMap,
        HashSet<string> allowCycleSet,
        HashSet<string> reportedCycles)
    {
        color[node] = 1; // 灰
        path.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                int neighborColor;
                color.TryGetValue(neighbor, out neighborColor);
                if (neighborColor == 1) // 遇到灰节点 = 发现环
                {
                    // 提取环：从path中找到neighbor的位置到当前
                    var cycleStart = path.IndexOf(neighbor);
                    var cycle = path.Skip(cycleStart).ToList();

                    // 检查[AllowCycle]豁免
                    var hasAllowCycle = cycle.Any(n => allowCycleSet.Contains(n));
                    if (!hasAllowCycle)
                    {
                        // 去重
                        var normalized = NormalizeCycleKey(cycle);
                        if (reportedCycles.Add(normalized))
                        {
                            var cycleStr = string.Join(" → ", cycle) + " → " + cycle[0];
                            var location = cycle.Select(n => locationMap[n]).FirstOrDefault(l => l is not null) ?? Location.None;
                            context.ReportDiagnostic(Diagnostic.Create(DiCycleRule, location, cycleStr));
                        }
                    }
                }
                else if (neighborColor == 0) // 白节点，继续DFS
                {
                    DfsCycle(neighbor, adjacency, color, path, context, locationMap, allowCycleSet, reportedCycles);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        color[node] = 2; // 黑
    }

    /// <summary>
    /// 环路径规范化：将环旋转到最小元素开头，用于去重（同一环不同起点只报一次）。
    /// </summary>
    private static string NormalizeCycleKey(List<string> cycle)
    {
        var minIdx = 0;
        for (var i = 1; i < cycle.Count; i++)
        {
            if (string.CompareOrdinal(cycle[i], cycle[minIdx]) < 0)
                minIdx = i;
        }
        var rotated = new string[cycle.Count];
        for (var i = 0; i < cycle.Count; i++)
            rotated[i] = cycle[(minIdx + i) % cycle.Count];
        return string.Join("->", rotated);
    }



    // 与 JoinCode.Abstractions.Attributes.ServiceLifetime 对齐的常量值
    // Singleton=0, Scoped=1, Transient=2
    private const int LifetimeSingleton = 0;
    private const int LifetimeScoped = 1;
    private const int LifetimeTransient = 2;

    private sealed class ServiceRegistration
    {
        public string? InterfaceName { get; }
        public int Lifetime { get; }

        public ServiceRegistration(string? interfaceName, int lifetime)
        {
            InterfaceName = interfaceName;
            Lifetime = lifetime;
        }
    }

    private sealed class ServiceRegistrationInfo
    {
        public string ImplementationName { get; }
        public List<ServiceRegistration> Registrations { get; }
        public List<ConstructorDependency> ConstructorDependencies { get; }
        public Location? Location { get; }
        public bool HasAllowCycle { get; }

        public ServiceRegistrationInfo(
            string implementationName,
            List<ServiceRegistration> registrations,
            List<ConstructorDependency> constructorDependencies,
            Location? location,
            bool hasAllowCycle)
        {
            ImplementationName = implementationName;
            Registrations = registrations;
            ConstructorDependencies = constructorDependencies;
            Location = location;
            HasAllowCycle = hasAllowCycle;
        }
    }

    /// <summary>
    /// 构造函数依赖项：依赖类型名 + 是否可选（有默认值或可空标注视为可选，环检测时跳过可选依赖）。
    /// </summary>
    private sealed class ConstructorDependency
    {
        public string DependencyType { get; }
        public bool IsOptional { get; }

        public ConstructorDependency(string dependencyType, bool isOptional)
        {
            DependencyType = dependencyType;
            IsOptional = isOptional;
        }
    }



    private static void VisitNamespacesForOptions(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol optionsAttr,
        List<OptionsRegistration> results,
        string currentAssemblyName,
        bool filterByCurrentAssembly)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespacesForOptions(childNamespace, optionsAttr, results, currentAssemblyName, filterByCurrentAssembly);
            else if (member is INamedTypeSymbol typeSymbol)
            {
                if (filterByCurrentAssembly && !IsFromCurrentAssembly(typeSymbol, currentAssemblyName))
                    continue;

                var reg = ExtractOptionsRegistration(typeSymbol, optionsAttr);
                if (reg is not null)
                    results.Add(reg);
            }
        }
    }

    private static OptionsRegistration? ExtractOptionsRegistration(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol? optionsAttr)
    {
        if (optionsAttr is null)
            return null;

        var attr = typeSymbol.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, optionsAttr));
        if (attr is null)
            return null;

        var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        string? configPath = null;
        var pathArg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "ConfigurationPath").Value;
        if (pathArg.Value is string pathStr)
            configPath = pathStr;

        var validateOnStart = false;
        var validateArg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "ValidateOnStart").Value;
        if (validateArg.Value is bool validateBool)
            validateOnStart = validateBool;

        return new OptionsRegistration(typeName, configPath, validateOnStart);
    }

    private static void GenerateOptionsRegistrationCode(SourceProductionContext context, string sanitizedAssemblyName, ImmutableArray<OptionsRegistration> registrations)
    {
        // 始终生成方法（即使为空），保证调用方编译通过
        var methodName = $"Add{sanitizedAssemblyName}AutoRegisteredOptions";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine();
        sb.AppendLine("namespace Core.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static partial class ServiceRegistration");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IServiceCollection {methodName}(this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var reg in registrations)
        {
            if (reg.ConfigurationPath is not null)
            {
                sb.AppendLine($"        services.AddOptions<{reg.TypeName}>()");
                sb.AppendLine($"            .BindConfiguration(\"{reg.ConfigurationPath}\")");
                if (reg.ValidateOnStart)
                    sb.AppendLine("            .ValidateOnStart();");
                else
                    sb.AppendLine("            .ValidateDataAnnotations();");
            }
            else
            {
                sb.AppendLine($"        services.AddOptions<{reg.TypeName}>();");
            }
            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedOptionsRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private sealed class OptionsRegistration
    {
        public string TypeName { get; }
        public string? ConfigurationPath { get; }
        public bool ValidateOnStart { get; }

        public OptionsRegistration(string typeName, string? configurationPath, bool validateOnStart)
        {
            TypeName = typeName;
            ConfigurationPath = configurationPath;
            ValidateOnStart = validateOnStart;
        }
    }

    // === 生成上下文类型 ===
    // 携带程序集名（已清理）与注册项列表，供 RegisterSourceOutput 使用
    // 注意: 使用 class 而非 record，因为生成器目标框架为 netstandard2.0，不支持 record

    private sealed class ServiceGenerationContext
    {
        public string SanitizedAssemblyName { get; }
        public ImmutableArray<ServiceRegistrationInfo> Services { get; }

        public ServiceGenerationContext(string sanitizedAssemblyName, ImmutableArray<ServiceRegistrationInfo> services)
        {
            SanitizedAssemblyName = sanitizedAssemblyName;
            Services = services;
        }
    }



    private sealed class OptionsGenerationContext
    {
        public string SanitizedAssemblyName { get; }
        public ImmutableArray<OptionsRegistration> Registrations { get; }

        public OptionsGenerationContext(string sanitizedAssemblyName, ImmutableArray<OptionsRegistration> registrations)
        {
            SanitizedAssemblyName = sanitizedAssemblyName;
            Registrations = registrations;
        }
    }
}
