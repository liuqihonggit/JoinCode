namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class JsonSerializerAotRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleSerializerMissingTypeInfo = new(
            "JCC1011",
            "AOT incompatible: JsonSerializer call missing JsonTypeInfo parameter",
            "JsonSerializer.{0}() 调用缺少 JsonTypeInfo 参数，在 NativeAOT 下会触发反射序列化异常。应使用 JsonSerializer.{0}(value, JsonContext.Default.T) 重载。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "NativeAOT disables reflection-based serialization. All JsonSerializer.Serialize/Deserialize/SerializeToElement calls must pass a JsonTypeInfo parameter (e.g., XxxJsonContext.Default.T). " +
            "Exceptions: (1) test code, (2) SerializeToElement with JsonElement/JsonNode parameter (no reflection), (3) calls passing JsonSerializerOptions with TypeInfoResolver.");

        private static readonly DiagnosticDescriptor RuleJsonSerializerOptionsNoResolver = new(
            "JCC1012",
            "AOT incompatible: JsonSerializerOptions created without TypeInfoResolver",
            "new JsonSerializerOptions {{ {0} }} 未设置 TypeInfoResolver，在 NativeAOT 下使用此 options 会触发反射序列化异常。应从 JsonContext.Default.Options 继承，或设置 TypeInfoResolver 属性。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "NativeAOT disables reflection-based serialization. new JsonSerializerOptions() defaults TypeInfoResolver to null, " +
            "any serialization using this options falls back to reflection. Correct approaches: " +
            "(1) Use JsonContext.Default.Options (includes TypeInfoResolver). " +
            "(2) new JsonSerializerOptions(JsonContext.Default.Options) { ... } (inherit then override). " +
            "(3) Explicitly set options.TypeInfoResolver = JsonContext.Default.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleSerializerMissingTypeInfo, RuleJsonSerializerOptionsNoResolver);

        private static readonly HashSet<string> SerializerMethodNames = new(StringComparer.Ordinal)
        {
            "Serialize", "Deserialize", "SerializeToElement", "DeserializeFromElement",
            "SerializeAsync", "DeserializeAsync",
            "SerializeToDocument", "SerializeToNode",
        };

        private static bool IsJsonSerializerType(INamedTypeSymbol? type)
        {
            if (type is null) return false;
            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return name is "global::System.Text.Json.JsonSerializer" or "System.Text.Json.JsonSerializer";
        }

        private static bool IsJsonSerializerOptionsType(ITypeSymbol? type)
        {
            if (type is null) return false;
            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return name is "global::System.Text.Json.JsonSerializerOptions" or "System.Text.Json.JsonSerializerOptions";
        }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeImplicitObjectCreation, SyntaxKind.ImplicitObjectCreationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not InvocationExpressionSyntax invocation) return;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol;
            if (symbol is not IMethodSymbol method) return;

            var containingType = method.ContainingType;
            if (!IsJsonSerializerType(containingType)) return;

            if (!SerializerMethodNames.Contains(method.Name)) return;

            if (HasJsonTypeInfoParameter(method)) return;

            if (IsInsideTestCode(ctx.Node)) return;

            if (IsJsonElementOnlySerializeToElement(method, invocation)) return;

            if (UsesOptionsWithResolver(invocation, ctx)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleSerializerMissingTypeInfo,
                invocation.GetLocation(),
                method.Name));
        }

        private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not ObjectCreationExpressionSyntax creation) return;

            var typeSymbol = ctx.SemanticModel.GetTypeInfo(creation.Type, ctx.CancellationToken).Type;

            if (typeSymbol is null)
            {
                var ctorSymbol = ctx.SemanticModel.GetSymbolInfo(creation, ctx.CancellationToken).Symbol;
                if (ctorSymbol is IMethodSymbol ctor)
                    typeSymbol = ctor.ContainingType;
            }

            if (typeSymbol is null) return;

            if (!IsJsonSerializerOptionsType(typeSymbol)) return;

            if (IsInsideTestCode(ctx.Node)) return;

            if (HasTypeInfoResolverInitializer(creation)) return;

            if (InheritsFromContextOptions(creation, ctx)) return;

            var details = ExtractOptionsDetails(creation);

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleJsonSerializerOptionsNoResolver,
                creation.GetLocation(),
                details));
        }

        private static void AnalyzeImplicitObjectCreation(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not ImplicitObjectCreationExpressionSyntax creation) return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(creation, ctx.CancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol ctor) return;

            var constructedType = ctor.ContainingType;
            if (constructedType is null) return;

            if (!IsJsonSerializerOptionsType(constructedType)) return;

            if (IsInsideTestCode(ctx.Node)) return;

            if (HasTypeInfoResolverInitializerForImplicit(creation)) return;

            var details = ExtractImplicitOptionsDetails(creation);

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleJsonSerializerOptionsNoResolver,
                creation.GetLocation(),
                details));
        }

        private static bool HasJsonTypeInfoParameter(IMethodSymbol method)
        {
            foreach (var param in method.Parameters)
            {
                var paramType = param.Type;
                if (paramType is null) continue;

                var fullName = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

if (fullName.StartsWith("global::System.Text.Json.JsonTypeInfo", StringComparison.Ordinal)
                    || fullName.StartsWith("System.Text.Json.JsonTypeInfo", StringComparison.Ordinal))
                    return true;

                if (fullName.StartsWith("global::System.Text.Json.Serialization.Metadata.JsonTypeInfo", StringComparison.Ordinal)
                    || fullName.StartsWith("System.Text.Json.Serialization.Metadata.JsonTypeInfo", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsInsideTestCode(SyntaxNode node)
        {
            return AotSafetyHelpers.IsInsideTestMethod(node);
        }

        private static bool IsJsonElementOnlySerializeToElement(IMethodSymbol method, InvocationExpressionSyntax invocation)
        {
            if (method.Name != "SerializeToElement") return false;

            if (method.Parameters.Length == 0) return false;

            if (method.IsGenericMethod)
            {
                var typeArgs = method.TypeArguments;
                if (typeArgs.Length > 0)
                {
                    var firstTypeArg = typeArgs[0];
                    var fullName = firstTypeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    if (fullName is "global::System.Text.Json.JsonElement" or "System.Text.Json.JsonElement"
                        or "global::System.Text.Json.Nodes.JsonNode" or "System.Text.Json.Nodes.JsonNode"
                        or "global::System.Text.Json.Nodes.JsonObject" or "System.Text.Json.Nodes.JsonObject"
                        or "global::System.Text.Json.Nodes.JsonArray" or "System.Text.Json.Nodes.JsonArray"
                        or "global::System.Text.Json.Nodes.JsonValue" or "System.Text.Json.Nodes.JsonValue")
                        return true;
                }
            }

            var firstParamType = method.Parameters[0].Type;
            if (firstParamType is null) return false;

            var paramFullName = firstParamType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (paramFullName is "global::System.Text.Json.JsonElement" or "System.Text.Json.JsonElement"
                or "global::System.Text.Json.Nodes.JsonNode" or "System.Text.Json.Nodes.JsonNode"
                or "global::System.Text.Json.Nodes.JsonObject" or "System.Text.Json.Nodes.JsonObject"
                or "global::System.Text.Json.Nodes.JsonArray" or "System.Text.Json.Nodes.JsonArray"
                or "global::System.Text.Json.Nodes.JsonValue" or "System.Text.Json.Nodes.JsonValue")
                return true;

            return false;
        }

        private static bool UsesOptionsWithResolver(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext ctx)
        {
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var argSymbol = ctx.SemanticModel.GetSymbolInfo(arg.Expression, ctx.CancellationToken).Symbol;

                if (argSymbol is IPropertySymbol property)
                {
                    if (IsJsonSerializerOptionsType(property.ContainingType))
                    {
                        if (property.Name == "TypeInfoResolver")
                            return true;
                    }
                }

                if (argSymbol is ILocalSymbol local)
                {
                    if (IsJsonSerializerOptionsType(local.Type))
                        return true;
                }

                if (argSymbol is IParameterSymbol param)
                {
                    if (IsJsonSerializerOptionsType(param.Type))
                        return true;
                }

                if (argSymbol is IFieldSymbol field)
                {
                    if (IsJsonSerializerOptionsType(field.Type))
                        return true;
                }
            }

            return false;
        }

        private static bool HasTypeInfoResolverInitializer(ObjectCreationExpressionSyntax creation)
        {
            foreach (var initializer in creation.Initializer?.Expressions ?? [])
            {
                if (initializer is AssignmentExpressionSyntax assignment)
                {
                    var left = assignment.Left.ToString().Trim();
                    if (left == "TypeInfoResolver")
                        return true;
                }
            }

            return false;
        }

        private static bool InheritsFromContextOptions(ObjectCreationExpressionSyntax creation, SyntaxNodeAnalysisContext ctx)
        {
            if (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0) return false;

            var firstArg = creation.ArgumentList.Arguments[0].Expression;
            var argSymbol = ctx.SemanticModel.GetSymbolInfo(firstArg, ctx.CancellationToken).Symbol;

            if (argSymbol is IPropertySymbol prop)
            {
                if (IsJsonSerializerOptionsType(prop.ContainingType))
                    return true;

                if (prop.Name == "Options" && prop.ContainingType?.AllInterfaces.Any(i =>
                    i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Contains("JsonSerializerContext", StringComparison.Ordinal)) == true)
                    return true;
            }

            return false;
        }

        private static string ExtractOptionsDetails(ObjectCreationExpressionSyntax creation)
        {
            var props = new List<string>();
            foreach (var initializer in creation.Initializer?.Expressions ?? [])
            {
                if (initializer is AssignmentExpressionSyntax assignment)
                {
                    props.Add(assignment.Left.ToString().Trim());
                }
            }

            if (creation.ArgumentList is not null && creation.ArgumentList.Arguments.Count > 0)
            {
                var firstArg = creation.ArgumentList.Arguments[0].ToString().Trim();
                if (!string.IsNullOrEmpty(firstArg))
                    props.Insert(0, $"从 {firstArg} 继承");
            }

            return props.Count > 0 ? string.Join(", ", props) : "无初始化器";
        }

        private static bool HasTypeInfoResolverInitializerForImplicit(ImplicitObjectCreationExpressionSyntax creation)
        {
            foreach (var initializer in creation.Initializer?.Expressions ?? [])
            {
                if (initializer is AssignmentExpressionSyntax assignment)
                {
                    var left = assignment.Left.ToString().Trim();
                    if (left == "TypeInfoResolver")
                        return true;
                }
            }

            return false;
        }

        private static string ExtractImplicitOptionsDetails(ImplicitObjectCreationExpressionSyntax creation)
        {
            var props = new List<string>();
            foreach (var initializer in creation.Initializer?.Expressions ?? [])
            {
                if (initializer is AssignmentExpressionSyntax assignment)
                {
                    props.Add(assignment.Left.ToString().Trim());
                }
            }

            if (creation.ArgumentList.Arguments.Count > 0)
            {
                var firstArg = creation.ArgumentList.Arguments[0].ToString().Trim();
                if (!string.IsNullOrEmpty(firstArg))
                    props.Insert(0, $"从 {firstArg} 继承");
            }

            return props.Count > 0 ? string.Join(", ", props) : "无初始化器";
        }
    }
}
