namespace AotHandler.Generator;

[Generator]
public sealed class AotHandlerRegistryGenerator : IIncrementalGenerator {
    private const string DispatchAttrFullName = "JoinCode.Abstractions.Attributes.McpToolDispatchAttribute";
    private const string ToolAttrFullName = "JoinCode.Abstractions.Attributes.McpToolAttribute";
    private const string ToolCategoryEnumFullName = "JoinCode.Abstractions.Utils.ToolCategory";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var handlerInfos = context.CompilationProvider
            .SelectMany(static (compilation, _) => {
                var dispatchAttr = compilation.GetTypeByMetadataName(DispatchAttrFullName);
                var toolAttr = compilation.GetTypeByMetadataName(ToolAttrFullName);
                var toolCategoryEnum = compilation.GetTypeByMetadataName(ToolCategoryEnumFullName);
                if (dispatchAttr is null || toolAttr is null)
                    return ImmutableArray<HandlerInfo>.Empty;

                var assemblyName = compilation.AssemblyName ?? "Unknown";
                var results = new List<HandlerInfo>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                VisitNamespaces(compilation.GlobalNamespace, dispatchAttr, toolAttr, toolCategoryEnum, assemblyName, results, visited);
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(handlerInfos, static (ctx, handlers) => {
            GenerateRegistrationCode(ctx, handlers);
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol dispatchAttr,
        INamedTypeSymbol toolAttr,
        INamedTypeSymbol? toolCategoryEnum,
        string assemblyName,
        List<HandlerInfo> results,
        HashSet<string> visited) {
        foreach (var member in namespaceSymbol.GetMembers()) {
            if (member is INamespaceSymbol childNamespace) {
                VisitNamespaces(childNamespace, dispatchAttr, toolAttr, toolCategoryEnum, assemblyName, results, visited);
            } else if (member is INamedTypeSymbol typeSymbol) {
                var attribute = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, dispatchAttr));
                if (attribute is null)
                    continue;

                var fullyQualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!visited.Add(fullyQualifiedName))
                    continue;

                var nodeId = ExtractNodeId(attribute, toolCategoryEnum);
                if (nodeId is null)
                    continue;

                var tools = new List<ToolMethodInfo>();
                foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>()) {
                    var toolAttribute = method.GetAttributes()
                        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, toolAttr));
                    if (toolAttribute is null)
                        continue;

                    var toolName = ExtractToolName(toolAttribute);
                    if (toolName is null)
                        continue;

                    var parameters = new List<ParamInfo>();
                    foreach (var param in method.Parameters) {
                        if (IsCancellationToken(param.Type) || IsToolProgressCallback(param.Type))
                            continue;
                        var paramName = param.Name ?? $"arg{parameters.Count}";
                        var paramType = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        parameters.Add(new ParamInfo(paramName, paramType));
                    }

                    tools.Add(new ToolMethodInfo(toolName, method.Name, parameters));
                }

                if (tools.Count > 0) {
                    results.Add(new HandlerInfo(fullyQualifiedName, nodeId, assemblyName, tools));
                }
            }
        }
    }

    private static string? ExtractNodeId(AttributeData attr, INamedTypeSymbol? toolCategoryEnum) {
        foreach (var namedArg in attr.NamedArguments) {
            if (namedArg.Key == "CategoryEnum" && namedArg.Value.Value is int enumValue) {
                return TryResolveEnumValue(toolCategoryEnum, enumValue);
            }
        }

        if (attr.ConstructorArguments.Length > 0) {
            var arg = attr.ConstructorArguments[0];
            if (arg.Value is string s)
                return s;
            if (arg.Type is not null && toolCategoryEnum is not null
                && SymbolEqualityComparer.Default.Equals(arg.Type, toolCategoryEnum)
                && arg.Value is int ev) {
                return TryResolveEnumValue(toolCategoryEnum, ev);
            }
        }

        return null;
    }

    private static string? ExtractToolName(AttributeData toolAttr) {
        if (toolAttr.ConstructorArguments.Length > 0) {
            var arg = toolAttr.ConstructorArguments[0];
            if (arg.Value is string s)
                return s;
        }
        return null;
    }

    private static string? TryResolveEnumValue(INamedTypeSymbol? enumType, int enumValue) {
        if (enumType is null || enumType.TypeKind != TypeKind.Enum)
            return null;

        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>()) {
            if (member.HasConstantValue && member.ConstantValue is int intValue && intValue == enumValue) {
                var enumValueAttr = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "EnumValueAttribute");
                if (enumValueAttr is not null) {
                    return enumValueAttr.ConstructorArguments.FirstOrDefault().Value as string;
                }
                return member.Name;
            }
        }
        return null;
    }

    private static bool IsCancellationToken(ITypeSymbol type) {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name == "System.Threading.CancellationToken"
            || name == "global::System.Threading.CancellationToken";
    }

    private static bool IsToolProgressCallback(ITypeSymbol type) {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name.Contains("ToolProgressCallback");
    }

    private static void GenerateRegistrationCode(SourceProductionContext ctx, ImmutableArray<HandlerInfo> handlers) {
        if (handlers.Length == 0)
            return;

        var assemblyName = handlers[0].AssemblyName;
        var safeAssemblyName = assemblyName.Replace(".", "_");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> AOT handler 注册代码 — 由 AotHandlerRegistryGenerator 生成");
        sb.AppendLine("// 扫描 [McpToolDispatch] + [McpTool] 特性，生成 nodeId → method → handler 路由表");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("namespace JoinCode.Plugins.Infrastructure.Generated;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// AOT handler 注册代码 — 由源码生成器从 [McpToolDispatch] + [McpTool] 特性自动生成");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public static class AotHandlerRegistrations_{safeAssemblyName}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 注册当前程序集中所有 AOT handler 到构建器");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static void Register(global::JoinCode.Abstractions.Tools.IAotHandlerRegistryBuilder builder)");
        sb.AppendLine("    {");

        foreach (var handler in handlers) {
            foreach (var tool in handler.Tools) {
                var paramArray = string.Join(", ", tool.Parameters.Select((p, i) => $"({p.TypeFullName})args[{i}]!"));
                sb.AppendLine($"        builder.AddHandler(\"{EscapeString(handler.NodeId)}\", \"{EscapeString(tool.ToolName)}\",");
                sb.AppendLine($"            (sp, args) => sp.GetRequiredService<{handler.FullyQualifiedName}>().{tool.MethodName}({paramArray}).GetAwaiter().GetResult());");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        ctx.AddSource($"AotHandlerRegistrations_{safeAssemblyName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EscapeString(string s) {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class HandlerInfo {
        public string FullyQualifiedName { get; }
        public string NodeId { get; }
        public string AssemblyName { get; }
        public List<ToolMethodInfo> Tools { get; }

        public HandlerInfo(string fullyQualifiedName, string nodeId, string assemblyName, List<ToolMethodInfo> tools) {
            FullyQualifiedName = fullyQualifiedName;
            NodeId = nodeId;
            AssemblyName = assemblyName;
            Tools = tools;
        }
    }

    private sealed class ToolMethodInfo {
        public string ToolName { get; }
        public string MethodName { get; }
        public List<ParamInfo> Parameters { get; }

        public ToolMethodInfo(string toolName, string methodName, List<ParamInfo> parameters) {
            ToolName = toolName;
            MethodName = methodName;
            Parameters = parameters;
        }
    }

    private sealed class ParamInfo {
        public string Name { get; }
        public string TypeFullName { get; }

        public ParamInfo(string name, string typeFullName) {
            Name = name;
            TypeFullName = typeFullName;
        }
    }
}
