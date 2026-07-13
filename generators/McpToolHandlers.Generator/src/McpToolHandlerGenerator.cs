
namespace McpToolHandlers.Generator;

[Generator]
public sealed class McpToolHandlerGenerator : IIncrementalGenerator
{
    private const string HandlerAttributeFullName = "JoinCode.Abstractions.Attributes.McpToolHandlerAttribute";
    private const string ToolAttributeFullName = "JoinCode.Abstractions.Attributes.McpToolAttribute";
    private const string ParamAttributeFullName = "JoinCode.Abstractions.Attributes.McpToolParameterAttribute";
    private const string OptionsAttributeFullName = "JoinCode.Abstractions.Attributes.McpToolOptionsAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlerTypes = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var assemblyName = compilation.AssemblyName ?? "Unknown";
                return assemblyName;
            })
            .Combine(context.CompilationProvider
            .SelectMany(static (compilation, _) =>
            {
                var handlerAttr = compilation.GetTypeByMetadataName(HandlerAttributeFullName);
                var toolAttr = compilation.GetTypeByMetadataName(ToolAttributeFullName);
                var paramAttr = compilation.GetTypeByMetadataName(ParamAttributeFullName);
                var optionsAttr = compilation.GetTypeByMetadataName(OptionsAttributeFullName);
                if (handlerAttr is null)
                    return ImmutableArray<HandlerInfo>.Empty;

                var results = new List<HandlerInfo>();
                VisitNamespaces(compilation, compilation.GlobalNamespace, handlerAttr, toolAttr, paramAttr, optionsAttr, results);
                return results.ToImmutableArray();
            })
            .Collect());

        context.RegisterSourceOutput(handlerTypes, static (ctx, pair) =>
        {
            var assemblyName = pair.Left;
            var handlers = pair.Right;
            GenerateRegistrationCode(ctx, handlers, assemblyName);
        });
    }

    private static void VisitNamespaces(
        Compilation compilation,
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol handlerAttr,
        INamedTypeSymbol? toolAttr,
        INamedTypeSymbol? paramAttr,
        INamedTypeSymbol? optionsAttr,
        List<HandlerInfo> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(compilation, childNamespace, handlerAttr, toolAttr, paramAttr, optionsAttr, results);
            else if (member is INamedTypeSymbol typeSymbol)
            {
                var attribute = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, handlerAttr));
                if (attribute is not null)
                {
                    var displayName = attribute.ConstructorArguments.FirstOrDefault().Value as string ?? typeSymbol.Name;
                    var optional = false;
                    foreach (var namedArg in attribute.NamedArguments)
                    {
                        if (namedArg.Key == "Optional" && namedArg.Value.Value is bool b)
                            optional = b;
                        else if (namedArg.Key == "CategoryEnum" && namedArg.Value.Value is int enumValue)
                        {
                            var resolved = TryResolveEnumValue(compilation, enumValue);
                            if (resolved is not null)
                                displayName = resolved;
                        }
                    }

                    var tools = new List<ToolMethodInfo>();
                    if (toolAttr is not null)
                    {
                        foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
                        {
                            var toolAttribute = method.GetAttributes()
                                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, toolAttr));
                            if (toolAttribute is not null)
                            {
                                var toolName = toolAttribute.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? method.Name;
                                var toolDescription = toolAttribute.ConstructorArguments.ElementAtOrDefault(1).Value as string ?? method.Name;
                                var toolCategory = toolAttribute.ConstructorArguments.ElementAtOrDefault(2).Value as string ?? "other";

                                var parameters = new List<ParamInfo>();
                                var optionsTypeNames = new Dictionary<string, string>();
                                var hasProgressCallback = false;
                                foreach (var param in method.Parameters)
                                {
                                    var paramTypeName = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                    if (IsCancellationToken(paramTypeName))
                                        continue;
                                    // 跳过 ToolProgressCallback? 参数 — 对齐 TS onProgress，由生成器自动传递
                                    if (IsToolProgressCallback(paramTypeName))
                                    {
                                        hasProgressCallback = true;
                                        continue;
                                    }

                                    // 检测 [McpToolOptions] 参数 — 展开其属性为工具参数
                                    var optionsAttrInstance = optionsAttr is not null
                                        ? param.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, optionsAttr))
                                        : null;

                                    if (optionsAttrInstance is not null && param.Type is INamedTypeSymbol optionsType)
                                    {
                                        var optionsParamName = param.Name ?? "options";
                                        optionsTypeNames[optionsParamName] = paramTypeName;

                                        foreach (var property in optionsType.GetMembers().OfType<IPropertySymbol>())
                                        {
                                            var propAttrInstance = property.GetAttributes()
                                                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, paramAttr));

                                            if (propAttrInstance is null)
                                                continue;

                                            var propDesc = propAttrInstance.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? property.Name;
                                            var propRequired = true;
                                            var propDefault = (string?)null;
                                            var propEnum = (string[]?)null;

                                            foreach (var named in propAttrInstance.NamedArguments)
                                            {
                                                if (named.Key == "Required" && named.Value.Value is bool r)
                                                    propRequired = r;
                                                else if (named.Key == "DefaultValue" && named.Value.Value is string dv)
                                                    propDefault = dv;
                                                else if (named.Key == "EnumValues" && named.Value.Values.Length > 0)
                                                    propEnum = named.Value.Values.Select(v => v.Value as string ?? "").ToArray();
                                            }

                                            var propTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                            var propJsonType = MapTypeToJsonType(property.Type);
                                            var propIsNullable = propTypeName.EndsWith("?") || propTypeName.StartsWith("System.Nullable<");
                                            parameters.Add(new ParamInfo(
                                                property.Name,
                                                propTypeName,
                                                propJsonType,
                                                propDesc,
                                                propRequired,
                                                propIsNullable,
                                                false,
                                                propDefault,
                                                propEnum,
                                                optionsParamName));
                                        }
                                        continue;
                                    }

                                    var paramAttrInstance = param.GetAttributes()
                                        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, paramAttr));

                                    var paramDesc = paramAttrInstance?.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? param.Name ?? "";
                                    var paramRequired = true;
                                    var paramDefault = (string?)null;
                                    var paramEnum = (string[]?)null;

                                    if (paramAttrInstance is not null)
                                    {
                                        foreach (var named in paramAttrInstance.NamedArguments)
                                        {
                                            if (named.Key == "Required" && named.Value.Value is bool r)
                                                paramRequired = r;
                                            else if (named.Key == "DefaultValue" && named.Value.Value is string dv)
                                                paramDefault = dv;
                                            else if (named.Key == "EnumValues" && named.Value.Values.Length > 0)
                                                paramEnum = named.Value.Values.Select(v => v.Value as string ?? "").ToArray();
                                        }
                                    }

                                    if (param.HasExplicitDefaultValue)
                                        paramRequired = false;

                                    var jsonType = MapTypeToJsonType(param.Type);
                                    var isNullable = paramTypeName.EndsWith("?") || paramTypeName.StartsWith("System.Nullable<");
                                    parameters.Add(new ParamInfo(
                                        param.Name ?? $"arg{parameters.Count}",
                                        paramTypeName,
                                        jsonType,
                                        paramDesc,
                                        paramRequired,
                                        isNullable,
                                        param.HasExplicitDefaultValue,
                                        paramDefault,
                                        paramEnum,
                                        null));
                                }

                                var returnTypeName = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                tools.Add(new ToolMethodInfo(toolName, toolDescription, toolCategory, method.Name, parameters, returnTypeName, optionsTypeNames, hasProgressCallback));
                            }
                        }
                    }

                    results.Add(new HandlerInfo(
                        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        typeSymbol.Name,
                        displayName,
                        optional,
                        tools));
                }
            }
        }
    }

    private static bool IsCancellationToken(string typeName)
        => typeName == "System.Threading.CancellationToken"
        || typeName == "global::System.Threading.CancellationToken";

    /// <summary>
    /// 检测是否为 ToolProgressCallback? 类型 — 对齐 TS onProgress 参数
    /// </summary>
    private static bool IsToolProgressCallback(string typeName)
        => typeName == "JoinCode.Abstractions.Tools.ToolProgressCallback"
        || typeName == "global::JoinCode.Abstractions.Tools.ToolProgressCallback"
        || typeName == "JoinCode.Abstractions.Tools.ToolProgressCallback?"
        || typeName == "global::JoinCode.Abstractions.Tools.ToolProgressCallback?";

    private static bool IsArrayType(string typeName, out string elementType)
    {
        // 匹配 string[], string[]?, global::System.String[], global::System.String[]?
        if (typeName.EndsWith("[]") || typeName.EndsWith("[]?"))
        {
            var baseName = typeName.EndsWith("[]?")
                ? typeName.Substring(0, typeName.Length - 3)
                : typeName.Substring(0, typeName.Length - 2);
            elementType = baseName;
            return true;
        }
        elementType = "";
        return false;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        // 将 global::System.String → string, global::System.Int32 → int 等
        return typeName switch
        {
            "global::System.String" or "global::System.String?" => typeName.EndsWith("?") ? "string?" : "string",
            "global::System.Int32" or "global::System.Int32?" => typeName.EndsWith("?") ? "int?" : "int",
            "global::System.Int64" or "global::System.Int64?" => typeName.EndsWith("?") ? "long?" : "long",
            "global::System.Int16" or "global::System.Int16?" => typeName.EndsWith("?") ? "short?" : "short",
            "global::System.Byte" or "global::System.Byte?" => typeName.EndsWith("?") ? "byte?" : "byte",
            "global::System.Double" or "global::System.Double?" => typeName.EndsWith("?") ? "double?" : "double",
            "global::System.Single" or "global::System.Single?" => typeName.EndsWith("?") ? "float?" : "float",
            "global::System.Decimal" or "global::System.Decimal?" => typeName.EndsWith("?") ? "decimal?" : "decimal",
            "global::System.Boolean" or "global::System.Boolean?" => typeName.EndsWith("?") ? "bool?" : "bool",
            _ => typeName
        };
    }

    private static string MapTypeToJsonType(ITypeSymbol type)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var simplified = GetSimpleTypeName(typeName);

        if (simplified == "string" || simplified == "string?")
            return "string";
        if (simplified == "int" || simplified == "long" || simplified == "short" || simplified == "byte"
            || simplified == "int?" || simplified == "long?" || simplified == "short?" || simplified == "byte?")
            return "integer";
        if (simplified == "double" || simplified == "float" || simplified == "decimal"
            || simplified == "double?" || simplified == "float?" || simplified == "decimal?")
            return "number";
        if (simplified == "bool" || simplified == "bool?")
            return "boolean";
        if (IsArrayType(typeName, out var elementType))
        {
            var simplifiedElement = GetSimpleTypeName(elementType);
            if (simplifiedElement == "string" || simplifiedElement == "string?")
                return "array:string";
            if (simplifiedElement == "int" || simplifiedElement == "long")
                return "array:integer";
            if (simplifiedElement == "double" || simplifiedElement == "float")
                return "array:number";
            if (simplifiedElement == "bool")
                return "array:boolean";
            if (IsDictionaryOfJsonElement(elementType))
                return "array:object";
            return "array:string";
        }

        return "string";
    }

    private static string GenerateArgExtractor(ParamInfo param)
    {
        var name = param.Name;
        var typeName = param.TypeName;
        var snakeName = ToSnakeCase(name);
        var simplified = GetSimpleTypeName(typeName);

        // 处理数组类型
        if (IsArrayType(typeName, out var elementType))
        {
            var simplifiedElement = GetSimpleTypeName(elementType);
            var isDictElement = IsDictionaryOfJsonElement(elementType);
            var elementExtractor = simplifiedElement switch
            {
                "string" or "string?" => "e.GetString() ?? \"\"",
                "int" or "int?" => "e.GetInt32()",
                "long" or "long?" => "e.GetInt64()",
                "double" or "double?" => "e.GetDouble()",
                "float" or "float?" => "e.GetSingle()",
                "bool" or "bool?" => "e.GetBoolean()",
                _ => isDictElement
                    ? "e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)"
                    : "e.GetString() ?? \"\""
            };
            var nullableSuffix = param.IsNullable ? "null" : $"Array.Empty<{GetSimpleTypeName(elementType).TrimEnd('?')}>()";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) && __{name}El.ValueKind == System.Text.Json.JsonValueKind.Array ? __{name}El.EnumerateArray().Select(e => {elementExtractor}).ToArray() : {nullableSuffix}";
        }

        // 使用简化类型名匹配，结合 IsNullable 判断
        var isNullableType = simplified.EndsWith("?") || param.IsNullable;
        var baseType = simplified.EndsWith("?") ? simplified.Substring(0, simplified.Length - 1) : simplified;

        if (baseType == "string")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetString() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetString() ?? \"\" : \"\"";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetString() ?? \"\" : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }
        if (baseType == "int")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? (int?)__{name}El.GetInt32() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetInt32() : 0";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetInt32() : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }
        if (baseType == "long")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? (long?)__{name}El.GetInt64() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetInt64() : 0L";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetInt64() : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }
        if (baseType == "double")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? (double?)__{name}El.GetDouble() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetDouble() : 0.0";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetDouble() : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }
        if (baseType == "float")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? (float?)__{name}El.GetSingle() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetSingle() : 0.0f";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetSingle() : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }
        if (baseType == "bool")
        {
            if (isNullableType)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? (bool?)__{name}El.GetBoolean() : null";
            if (!param.Required)
                return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetBoolean() : false";
            return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetBoolean() : throw new System.ArgumentException(\"Missing required parameter: {snakeName}\")";
        }

        // 可选参数有默认值时，不传则用默认值
        if (param.HasDefaultValue)
            return $"default({typeName})";

        // 非必需参数不传时返回默认值而非抛异常
        if (!param.Required)
            return $"default({typeName})";

        return $"args.TryGetValue(\"{snakeName}\", out var __{name}El) ? __{name}El.GetString() ?? \"\" : \"\"";
    }

    /// <summary>
    /// 判断类型名是否为 Dictionary&lt;string, JsonElement&gt;
    /// </summary>
    private static bool IsDictionaryOfJsonElement(string typeName)
        => typeName.Contains("Dictionary") && typeName.Contains("JsonElement");

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static void GenerateRegistrationCode(SourceProductionContext context, ImmutableArray<HandlerInfo> handlers, string assemblyName)
    {
        var validHandlers = handlers.OrderBy(h => h.DisplayName).ToList();

        if (validHandlers.Count == 0)
            return;

        var suffix = SanitizeAssemblyName(assemblyName);
        var className = $"GeneratedToolHandlerRegistration_{suffix}";
        var fileName = $"GeneratedToolHandlerRegistration_{suffix}.g.cs";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using JoinCode.Abstractions.Mcp.Registry;");
        sb.AppendLine("using JoinCode.Abstractions.Tools;");
        sb.AppendLine("using JoinCode.Abstractions.Utils.Diagnostics;");
        sb.AppendLine();
        sb.AppendLine("namespace McpToolHandlers;");
        sb.AppendLine();
        sb.AppendLine($"internal static partial class {className}");
        sb.AppendLine("{");

        GenerateAddSingletonsMethod(sb, validHandlers);
        sb.AppendLine();
        GenerateRegisterAllMethod(sb, validHandlers);
        sb.AppendLine();
        GeneratePerHandlerRegisterMethods(sb, validHandlers);
        sb.AppendLine();
        GenerateToolCategoriesMethod(sb, validHandlers);

        sb.AppendLine("}");

        context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string SanitizeAssemblyName(string assemblyName)
    {
        var sb = new StringBuilder(assemblyName.Length);
        foreach (var c in assemblyName)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c == '.' || c == '-' || c == '_')
                sb.Append('_');
        }
        return sb.ToString();
    }

    private static void GenerateAddSingletonsMethod(StringBuilder sb, List<HandlerInfo> handlers)
    {
        sb.AppendLine("    internal static IServiceCollection AddMcpToolHandlerSingletons(this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var handler in handlers)
        {
            sb.AppendLine($"        services.AddSingleton<{handler.FullyQualifiedName}>();");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
    }

    private static void GenerateRegisterAllMethod(StringBuilder sb, List<HandlerInfo> handlers)
    {
        sb.AppendLine($"    public static async Task<IMcpToolRegistry> RegisterAllMcpToolHandlersAsync(");
        sb.AppendLine("        this IMcpToolRegistry registry,");
        sb.AppendLine("        IServiceProvider serviceProvider,");
        sb.AppendLine("        CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        var logger = serviceProvider.GetService<ILogger<IMcpToolRegistry>>();");
        sb.AppendLine("        var initialCount = await registry.GetCountAsync(cancellationToken);");
        sb.AppendLine();

        var handlerIndex = 0;
        foreach (var handler in handlers)
        {
            handlerIndex++;
            var optionalArg = handler.Optional ? "true" : "false";
            sb.AppendLine($"        Diag.WriteLine(\"[{handlerIndex}/{handlers.Count}] Registering {handler.DisplayName}...\");");
            sb.AppendLine($"        await Register{handler.TypeName}ToolsAsync(registry, serviceProvider, logger, cancellationToken, {optionalArg});");
            sb.AppendLine($"        Diag.WriteLine(\"[{handlerIndex}/{handlers.Count}] {handler.DisplayName} done\");");
        }

        sb.AppendLine();
        sb.AppendLine("        var finalCount = await registry.GetCountAsync(cancellationToken);");
        sb.AppendLine("        var totalRegistered = finalCount - initialCount;");
        sb.AppendLine("        logger?.LogInformation(\"MCP Tool Handlers 注册完成，共 {TotalCount} 个工具\", totalRegistered);");
        sb.AppendLine();
        sb.AppendLine("        return registry;");
        sb.AppendLine("    }");
    }

    private static void GeneratePerHandlerRegisterMethods(StringBuilder sb, List<HandlerInfo> handlers)
    {
        foreach (var handler in handlers)
        {
            GenerateSingleHandlerMethod(sb, handler);
            sb.AppendLine();
        }
    }

    private static void GenerateSingleHandlerMethod(StringBuilder sb, HandlerInfo handler)
    {
        sb.AppendLine($"    private static async Task Register{handler.TypeName}ToolsAsync(");
        sb.AppendLine("        IMcpToolRegistry registry,");
        sb.AppendLine("        IServiceProvider serviceProvider,");
        sb.AppendLine("        ILogger<IMcpToolRegistry>? logger,");
        sb.AppendLine("        CancellationToken cancellationToken,");
        sb.AppendLine("        bool optional)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {handler.FullyQualifiedName}? handler;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            handler = serviceProvider.GetService<{handler.FullyQualifiedName}>();");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (InvalidOperationException)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (optional)");
        sb.AppendLine($"                logger?.LogDebug(\"  ⊙ {handler.DisplayName}: 依赖服务未注册，已跳过\");");
        sb.AppendLine("            else");
        sb.AppendLine($"                logger?.LogWarning(\"  \u2717 {handler.DisplayName}: 依赖服务未注册\");");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (handler == null)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (optional)");
        sb.AppendLine($"                logger?.LogDebug(\"  \u25CB {handler.DisplayName}: 服务未注册，已跳过\");");
        sb.AppendLine("            else");
        sb.AppendLine($"                logger?.LogWarning(\"  \u2717 {handler.DisplayName}: 服务未注册\");");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine();

        foreach (var tool in handler.Tools)
        {
            GenerateToolRegistration(sb, handler, tool);
        }

        sb.AppendLine($"        logger?.LogInformation(\"  \u2713 {handler.DisplayName}: {handler.Tools.Count} 个\");");
        sb.AppendLine("    }");
    }

    private static void GenerateToolRegistration(StringBuilder sb, HandlerInfo handler, ToolMethodInfo tool)
    {
        sb.AppendLine("        {");
        sb.AppendLine($"            var __schema = new ToolSchema");
        sb.AppendLine("            {");
        sb.AppendLine("                Type = \"object\",");
        sb.AppendLine("                Properties = new Dictionary<string, ToolSchemaProperty>");
        sb.AppendLine("                {");

        foreach (var param in tool.Parameters)
        {
            var jsonType = param.JsonType;
            var isArray = jsonType.StartsWith("array:");
            var actualType = isArray ? jsonType.Substring(6) : jsonType;

            sb.AppendLine($"                    [\"{ToSnakeCase(param.Name)}\"] = new ToolSchemaProperty");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        Type = \"{(isArray ? "array" : actualType)}\",");
            sb.AppendLine($"                        Description = \"{EscapeString(param.Description)}\",");
            if (isArray)
            {
                sb.AppendLine($"                        Items = new ToolSchemaProperty {{ Type = \"{actualType}\" }},");
            }
            if (param.EnumValues is not null && param.EnumValues.Length > 0)
            {
                sb.AppendLine($"                        Enum = new List<string> {{ {string.Join(", ", param.EnumValues.Select(e => $"\"{EscapeString(e)}\""))} }},");
            }
            if (param.DefaultValue is not null)
            {
                sb.AppendLine($"                        Default = \"{EscapeString(param.DefaultValue)}\",");
            }
            sb.AppendLine("                    },");
        }

        sb.AppendLine("                },");
        var requiredParams = tool.Parameters.Where(p => p.Required).ToList();
        if (requiredParams.Count > 0)
        {
            sb.AppendLine($"                Required = new List<string> {{ {string.Join(", ", requiredParams.Select(p => $"\"{ToSnakeCase(p.Name)}\""))} }},");
        }
        sb.AppendLine("            };");
        sb.AppendLine();

        var argExtractors = tool.Parameters.Select(p => GenerateArgExtractor(p)).ToList();
        var argsList = string.Join(", ", argExtractors);

        // 处理 [McpToolOptions] 参数：将展开的属性参数合并为对象初始化器
        if (tool.OptionsTypeNames.Count > 0)
        {
            var invocationArgs = new List<string>();
            var i = 0;
            while (i < tool.Parameters.Count)
            {
                var param = tool.Parameters[i];
                if (param.OptionsParamName is not null)
                {
                    // 收集同一 [McpToolOptions] 参数的所有展开属性
                    var optionsName = param.OptionsParamName;
                    var optionsParams = new List<(ParamInfo Param, string Extractor)>();
                    while (i < tool.Parameters.Count && tool.Parameters[i].OptionsParamName == optionsName)
                    {
                        optionsParams.Add((tool.Parameters[i], argExtractors[i]));
                        i++;
                    }
                    // 生成对象初始化器: new TypeName { Prop1 = extractor1, Prop2 = extractor2, ... }
                    var typeName = tool.OptionsTypeNames[optionsName];
                    var propertyInitializers = optionsParams.Select(p => $"{p.Param.Name} = {p.Extractor}");
                    invocationArgs.Add($"new {typeName} {{ {string.Join(", ", propertyInitializers)} }}");
                }
                else
                {
                    invocationArgs.Add(argExtractors[i]);
                    i++;
                }
            }
            argsList = string.Join(", ", invocationArgs);
        }

        if (tool.Parameters.Any(p => p.Name == "cancellationToken") == false)
        {
            argsList = string.IsNullOrEmpty(argsList) ? "ct" : argsList + ", ct";
        }

        // 对齐 TS onProgress: 仅当方法签名包含 ToolProgressCallback? 时才传递 onProgress
        if (tool.HasProgressCallback)
        {
            argsList = string.IsNullOrEmpty(argsList) ? "onProgress" : argsList + ", onProgress";
        }

        sb.AppendLine($"            await registry.RegisterToolAsync(\"{tool.Name}\", \"{EscapeString(tool.Description)}\", __schema,");
        sb.AppendLine($"                async (__name, args, ct, onProgress) => await handler.{tool.MethodName}({argsList}),");
        sb.AppendLine("                cancellationToken);");
        sb.AppendLine("        }");
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void GenerateToolCategoriesMethod(StringBuilder sb, List<HandlerInfo> handlers)
    {
        sb.AppendLine($"    public static Dictionary<string, List<(string Name, string Description)>> GetAvailableToolCategories()");
        sb.AppendLine("    {");
        sb.AppendLine("        var categories = new Dictionary<string, List<(string Name, string Description)>>(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine();

        foreach (var handler in handlers)
        {
            if (handler.Tools.Count == 0) continue;

            var displayName = EscapeString(handler.DisplayName);

            foreach (var tool in handler.Tools)
            {
                var toolName = EscapeString(tool.Name);
                var toolDesc = EscapeString(tool.Description);
                sb.AppendLine($"        if (!categories.ContainsKey(\"{displayName}\")) categories[\"{displayName}\"] = new List<(string, string)>();");
                sb.AppendLine($"        categories[\"{displayName}\"].Add((\"{toolName}\", \"{toolDesc}\"));");
            }

            sb.AppendLine();
        }

        sb.AppendLine("        return categories;");
        sb.AppendLine("    }");
    }

    private sealed class HandlerInfo
    {
        public string FullyQualifiedName { get; }
        public string TypeName { get; }
        public string DisplayName { get; }
        public bool Optional { get; }
        public List<ToolMethodInfo> Tools { get; }

        public HandlerInfo(string fullyQualifiedName, string typeName, string displayName, bool optional, List<ToolMethodInfo> tools)
        {
            FullyQualifiedName = fullyQualifiedName;
            TypeName = typeName;
            DisplayName = displayName;
            Optional = optional;
            Tools = tools;
        }
    }

    private sealed class ToolMethodInfo
    {
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }
        public string MethodName { get; }
        public List<ParamInfo> Parameters { get; }
        public string ReturnTypeName { get; }
        /// <summary>
        /// [McpToolOptions] 参数名 → 完全限定类型名的映射
        /// </summary>
        public Dictionary<string, string> OptionsTypeNames { get; }
        /// <summary>
        /// 方法签名是否包含 ToolProgressCallback? 参数 — 对齐 TS onProgress
        /// </summary>
        public bool HasProgressCallback { get; }

        public ToolMethodInfo(string name, string description, string category, string methodName, List<ParamInfo> parameters, string returnTypeName, Dictionary<string, string> optionsTypeNames, bool hasProgressCallback)
        {
            Name = name;
            Description = description;
            Category = category;
            MethodName = methodName;
            Parameters = parameters;
            ReturnTypeName = returnTypeName;
            OptionsTypeNames = optionsTypeNames;
            HasProgressCallback = hasProgressCallback;
        }
    }

    private sealed class ParamInfo
    {
        public string Name { get; }
        public string TypeName { get; }
        public string JsonType { get; }
        public string Description { get; }
        public bool Required { get; }
        public bool IsNullable { get; }
        public bool HasDefaultValue { get; }
        public string? DefaultValue { get; }
        public string[]? EnumValues { get; }
        /// <summary>
        /// 如果该参数从 [McpToolOptions] 参数的属性展开而来，记录原始方法参数名；否则为 null
        /// </summary>
        public string? OptionsParamName { get; }

        public ParamInfo(string name, string typeName, string jsonType, string description, bool required, bool isNullable, bool hasDefaultValue, string? defaultValue, string[]? enumValues, string? optionsParamName = null)
        {
            Name = name;
            TypeName = typeName;
            JsonType = jsonType;
            Description = description;
            Required = required;
            IsNullable = isNullable;
            HasDefaultValue = hasDefaultValue;
            DefaultValue = defaultValue;
            EnumValues = enumValues;
            OptionsParamName = optionsParamName;
        }
    }

    private static string? TryResolveEnumValue(Compilation compilation, int enumValue)
    {
        var enumType = compilation.GetTypeByMetadataName("JoinCode.Abstractions.Utils.ToolCategory");
        if (enumType is null || enumType.TypeKind != TypeKind.Enum)
            return null;

        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue && member.ConstantValue is int intValue && intValue == enumValue)
            {
                var enumValueAttr = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "EnumValueAttribute");
                if (enumValueAttr is not null)
                {
                    return enumValueAttr.ConstructorArguments.FirstOrDefault().Value as string;
                }
                return member.Name;
            }
        }
        return null;
    }
}
