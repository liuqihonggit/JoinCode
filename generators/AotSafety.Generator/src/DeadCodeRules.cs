namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeadCodeRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleOrphanMethod = new(
            "JCC7001",
            "死代码: 方法 '{0}' 未被任何代码引用",
            "方法 '{0}' 在当前编译中未被引用。请先分析是否为分析器盲区（如源码生成器调用、事件订阅、方法组引用等），若是则修复分析器屏蔽规则；确认非误报后再考虑移除或使用 #pragma warning disable JCC7001 抑制。",
            "DeadCodeAudit",
            DiagnosticSeverity.Info,
            true,
            "未被引用的 private/internal 方法可能是遗留代码或分析器盲区. 优先修复分析器屏蔽规则而非抑制警告. 接口实现、重写方法、序列化构造函数、源码生成器特性标注、事件订阅等自动排除.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleOrphanType = new(
            "JCC7002",
            "死代码: 类型 '{0}' 未被任何代码引用",
            "类型 '{0}' 在当前编译中未被引用。请先分析是否为分析器盲区，若是则修复分析器屏蔽规则；确认非误报后再考虑移除或使用 #pragma warning disable JCC7002 抑制。",
            "DeadCodeAudit",
            DiagnosticSeverity.Info,
            true,
            "未被引用的 internal 类型可能是遗留代码或分析器盲区. 优先修复分析器屏蔽规则而非抑制警告. 含 public 成员的类型自动排除.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleOrphanEnum = new(
            "JCC7003",
            "死代码: 枚举 '{0}' 未被任何代码引用",
            "枚举 '{0}' 在当前编译中未被引用。请先分析是否为分析器盲区，若是则修复分析器屏蔽规则；确认非误报后再考虑移除或使用 #pragma warning disable JCC7003 抑制。",
            "DeadCodeAudit",
            DiagnosticSeverity.Info,
            true,
            "未被引用的 internal 枚举可能是遗留代码或分析器盲区. 优先修复分析器屏蔽规则而非抑制警告. 含 [Flags] 特性的枚举自动排除.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleOrphanMethod, RuleOrphanType, RuleOrphanEnum);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(AnalyzeDeadCode);
        }

        private static void AnalyzeDeadCode(CompilationStartAnalysisContext context)
        {
            var methodDeclarations = new ConcurrentDictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
            var typeDeclarations = new ConcurrentDictionary<INamedTypeSymbol, BaseTypeDeclarationSyntax>(SymbolEqualityComparer.Default);
            var enumDeclarations = new ConcurrentDictionary<INamedTypeSymbol, EnumDeclarationSyntax>(SymbolEqualityComparer.Default);

            var referencedMethods = new ConcurrentDictionary<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
            var referencedTypes = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
            var referencedEnums = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);

            var attributeMethodNames = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            var syntaxReferencedMethodNames = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            var eventSubscribedMethodNames = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var method = (MethodDeclarationSyntax)ctx.Node;
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(method, ctx.CancellationToken) as IMethodSymbol;
                if (symbol is null) return;

                if (symbol.DeclaredAccessibility != Accessibility.Private &&
                    symbol.DeclaredAccessibility != Accessibility.Internal &&
                    symbol.DeclaredAccessibility != Accessibility.ProtectedAndInternal)
                    return;

                methodDeclarations[symbol] = method;
            }, SyntaxKind.MethodDeclaration);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var typeNode = (BaseTypeDeclarationSyntax)ctx.Node;
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeNode, ctx.CancellationToken) as INamedTypeSymbol;
                if (symbol is null) return;

                if (symbol.DeclaredAccessibility != Accessibility.Internal)
                    return;

                if (symbol.TypeKind == TypeKind.Enum) return;

                if (symbol.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Equals("global::System.CodeDom.Compiler.GeneratedCodeAttribute", StringComparison.Ordinal)))
                    return;

                typeDeclarations[symbol] = typeNode;
            }, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var enumNode = (EnumDeclarationSyntax)ctx.Node;
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(enumNode, ctx.CancellationToken) as INamedTypeSymbol;
                if (symbol is null) return;

                if (symbol.DeclaredAccessibility != Accessibility.Internal &&
                    symbol.DeclaredAccessibility != Accessibility.Private &&
                    symbol.DeclaredAccessibility != Accessibility.ProtectedAndInternal)
                    return;

                if (symbol.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Equals("global::System.FlagsAttribute", StringComparison.Ordinal)))
                    return;

                if (symbol.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Equals("global::System.CodeDom.Compiler.GeneratedCodeAttribute", StringComparison.Ordinal)))
                    return;

                enumDeclarations[symbol] = enumNode;
            }, SyntaxKind.EnumDeclaration);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var invocation = (InvocationExpressionSyntax)ctx.Node;
                var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken);
                var symbol = symbolInfo.Symbol;

                if (symbol is null && symbolInfo.CandidateSymbols.Length > 0)
                {
                    symbol = symbolInfo.CandidateSymbols[0];
                }

                if (symbol is IMethodSymbol methodSymbol)
                {
                    referencedMethods.TryAdd(methodSymbol, 0);
                    if (methodSymbol.IsGenericMethod && !methodSymbol.IsDefinition)
                        referencedMethods.TryAdd(methodSymbol.ConstructedFrom, 0);
                    if (methodSymbol.OverriddenMethod is not null)
                        referencedMethods.TryAdd(methodSymbol.OverriddenMethod, 0);
                    foreach (var ifaceMethod in methodSymbol.ExplicitInterfaceImplementations)
                        referencedMethods.TryAdd(ifaceMethod, 0);
                }

                var syntaxName = GetInvocationMethodName(invocation);
                if (syntaxName is not null)
                    syntaxReferencedMethodNames.TryAdd(syntaxName, 0);
            }, SyntaxKind.InvocationExpression);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is not IdentifierNameSyntax identifierName) return;

                if (identifierName.Parent is InvocationExpressionSyntax) return;

                if (identifierName.Parent is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name == identifierName &&
                    memberAccess.Parent is InvocationExpressionSyntax)
                    return;

                var symbol = ctx.SemanticModel.GetSymbolInfo(identifierName, ctx.CancellationToken).Symbol;
                if (symbol is IMethodSymbol methodGroupSymbol)
                {
                    referencedMethods.TryAdd(methodGroupSymbol, 0);
                }
            }, SyntaxKind.IdentifierName);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var attr = (AttributeSyntax)ctx.Node;
                if (attr.ArgumentList is null) return;

                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    if (arg.Expression is not LiteralExpressionSyntax literal) continue;
                    if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                    var value = literal.Token.ValueText;
                    if (!string.IsNullOrEmpty(value))
                        attributeMethodNames.TryAdd(value, 0);
                }
            }, SyntaxKind.Attribute);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is not AssignmentExpressionSyntax assignment) return;
                if (!assignment.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken)) return;

                var right = assignment.Right;
                string? methodName = null;
                if (right is IdentifierNameSyntax idName)
                    methodName = idName.Identifier.ValueText;
                else if (right is MemberAccessExpressionSyntax memberAccess)
                    methodName = memberAccess.Name.Identifier.ValueText;

                if (methodName is not null)
                    eventSubscribedMethodNames.TryAdd(methodName, 0);
            }, SyntaxKind.AddAssignmentExpression);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var objCreation = (ObjectCreationExpressionSyntax)ctx.Node;
                var typeInfo = ctx.SemanticModel.GetTypeInfo(objCreation, ctx.CancellationToken).Type;
                AddReferencedType(referencedTypes, typeInfo);
                AddReferencedEnum(referencedEnums, typeInfo);
            }, SyntaxKind.ObjectCreationExpression);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is VariableDeclarationSyntax varDecl)
                {
                    var typeInfo = ctx.SemanticModel.GetTypeInfo(varDecl.Type, ctx.CancellationToken).Type;
                    AddReferencedType(referencedTypes, typeInfo);
                    AddReferencedEnum(referencedEnums, typeInfo);
                }
            }, SyntaxKind.VariableDeclaration);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is ParameterSyntax param && param.Type is not null)
                {
                    var typeInfo = ctx.SemanticModel.GetTypeInfo(param.Type, ctx.CancellationToken).Type;
                    AddReferencedType(referencedTypes, typeInfo);
                    AddReferencedEnum(referencedEnums, typeInfo);
                }
            }, SyntaxKind.Parameter);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is MemberAccessExpressionSyntax memberAccess)
                {
                    var symbolInfo = ctx.SemanticModel.GetSymbolInfo(memberAccess, ctx.CancellationToken).Symbol;
                    if (symbolInfo is IFieldSymbol fieldSymbol &&
                        fieldSymbol.ContainingType is not null &&
                        fieldSymbol.ContainingType.TypeKind == TypeKind.Enum)
                    {
                        referencedEnums.TryAdd(fieldSymbol.ContainingType, 0);
                    }
                }
            }, SyntaxKind.SimpleMemberAccessExpression);

            context.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                if (ctx.Node is TypeOfExpressionSyntax typeOfExpr)
                {
                    var typeInfo = ctx.SemanticModel.GetTypeInfo(typeOfExpr.Type, ctx.CancellationToken).Type;
                    AddReferencedEnum(referencedEnums, typeInfo);
                }
            }, SyntaxKind.TypeOfExpression);

            context.RegisterCompilationEndAction(ctx =>
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                foreach (var kvp in methodDeclarations)
                {
                    var symbol = kvp.Key;
                    var syntax = kvp.Value;

                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    if (symbol.IsStatic && symbol.Name == "Main") continue;

                    if (symbol.GetAttributes().Any(attr =>
                        attr.AttributeClass is not null &&
                        attr.AttributeClass.Name == "ModuleInitializerAttribute"))
                        continue;

                    if (symbol.ExplicitInterfaceImplementations.Length > 0) continue;

                    if (IsExplicitInterfaceImplementationSyntax(syntax)) continue;

                    if (IsInterfaceImplementation(symbol)) continue;

                    if (symbol.IsOverride) continue;

                    if (symbol.MethodKind == MethodKind.UserDefinedOperator) continue;

                    if (symbol.MethodKind == MethodKind.PropertyGet || symbol.MethodKind == MethodKind.PropertySet) continue;

                    if (symbol.MethodKind == MethodKind.EventAdd || symbol.MethodKind == MethodKind.EventRemove) continue;

                    if (symbol.MethodKind == MethodKind.Constructor || symbol.MethodKind == MethodKind.StaticConstructor) continue;

                    if (symbol.MethodKind == MethodKind.Destructor) continue;

                    if (symbol.DeclaredAccessibility == Accessibility.Internal ||
                        symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                    {
                        if (HasInternalsVisibleTo(symbol.ContainingAssembly))
                            continue;
                    }

                    if (IsMethodReferenced(symbol, referencedMethods)) continue;

                    if (attributeMethodNames.ContainsKey(symbol.Name)) continue;

                    if (syntaxReferencedMethodNames.ContainsKey(symbol.Name)) continue;

                    if (eventSubscribedMethodNames.ContainsKey(symbol.Name)) continue;

                    if (HasCustomAttribute(symbol)) continue;

                    var methodName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    ctx.ReportDiagnostic(Diagnostic.Create(RuleOrphanMethod, syntax.Identifier.GetLocation(), methodName));
                }

                foreach (var kvp in typeDeclarations)
                {
                    var symbol = kvp.Key;
                    var syntax = kvp.Value;

                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    if (HasPublicMembers(symbol)) continue;

                    if (symbol.DeclaredAccessibility == Accessibility.Internal ||
                        symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                    {
                        if (HasInternalsVisibleTo(symbol.ContainingAssembly))
                            continue;
                    }

                    if (IsTypeReferenced(symbol, referencedTypes)) continue;

                    var typeNameStr = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var location = syntax.Identifier.GetLocation();
                    ctx.ReportDiagnostic(Diagnostic.Create(RuleOrphanType, location, typeNameStr));
                }

                foreach (var kvp in enumDeclarations)
                {
                    var symbol = kvp.Key;
                    var syntax = kvp.Value;

                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    if (referencedEnums.ContainsKey(symbol)) continue;

                    var enumName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    ctx.ReportDiagnostic(Diagnostic.Create(RuleOrphanEnum, syntax.Identifier.GetLocation(), enumName));
                }
            });
        }

        private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is IdentifierNameSyntax directName)
                return directName.Identifier.ValueText;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.ValueText;

            return null;
        }

        private static bool IsInterfaceImplementation(IMethodSymbol method)
        {
            if (method.ExplicitInterfaceImplementations.Length > 0) return true;

            var containingType = method.ContainingType;
            if (containingType is null) return false;

            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers(method.Name))
                {
                    if (ifaceMember is IMethodSymbol ifaceMethod)
                    {
                        if (method.Parameters.Length == ifaceMethod.Parameters.Length)
                        {
                            var paramsMatch = true;
                            for (var i = 0; i < method.Parameters.Length; i++)
                            {
                                if (!method.Parameters[i].Type.Equals(ifaceMethod.Parameters[i].Type, SymbolEqualityComparer.Default))
                                {
                                    paramsMatch = false;
                                    break;
                                }
                            }
                            if (paramsMatch) return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool HasInternalsVisibleTo(IAssemblySymbol? assembly)
        {
            if (assembly is null) return false;
            foreach (var attr in assembly.GetAttributes())
            {
                if (attr.AttributeClass is not null &&
                    attr.AttributeClass.Name == "InternalsVisibleToAttribute")
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsMethodReferenced(IMethodSymbol method, ConcurrentDictionary<IMethodSymbol, byte> referencedMethods)
        {
            if (referencedMethods.ContainsKey(method)) return true;

            if (method.PartialDefinitionPart is not null && referencedMethods.ContainsKey(method.PartialDefinitionPart)) return true;
            if (method.PartialImplementationPart is not null && referencedMethods.ContainsKey(method.PartialImplementationPart)) return true;

            if (method.IsGenericMethod && !method.IsDefinition)
            {
                if (referencedMethods.ContainsKey(method.ConstructedFrom)) return true;
            }

            return false;
        }

        private static bool HasCustomAttribute(IMethodSymbol method)
        {
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass is null) continue;

                var ns = attr.AttributeClass.ContainingNamespace;
                if (ns is not null)
                {
                    var nsName = ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (nsName.StartsWith("global::System", StringComparison.Ordinal))
                        continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsExplicitInterfaceImplementationSyntax(MethodDeclarationSyntax syntax)
        {
            return syntax.ExplicitInterfaceSpecifier is not null;
        }

        private static bool IsTypeReferenced(INamedTypeSymbol type, ConcurrentDictionary<INamedTypeSymbol, byte> referencedTypes)
        {
            return referencedTypes.ContainsKey(type);
        }

        private static void AddReferencedType(ConcurrentDictionary<INamedTypeSymbol, byte> referencedTypes, ITypeSymbol? typeInfo)
        {
            if (typeInfo is not INamedTypeSymbol namedType) return;
            referencedTypes.TryAdd(namedType, 0);
            if (namedType.ConstructedFrom is not null && !namedType.ConstructedFrom.Equals(namedType, SymbolEqualityComparer.Default))
                referencedTypes.TryAdd(namedType.ConstructedFrom, 0);
        }

        private static void AddReferencedEnum(ConcurrentDictionary<INamedTypeSymbol, byte> referencedEnums, ITypeSymbol? typeInfo)
        {
            if (typeInfo is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Enum)
            {
                referencedEnums.TryAdd(namedType, 0);
            }
        }

        private static bool HasPublicMembers(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.DeclaredAccessibility == Accessibility.Public ||
                    member.DeclaredAccessibility == Accessibility.Protected ||
                    member.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
                {
                    if (member is IMethodSymbol method &&
                        (method.MethodKind == MethodKind.Constructor ||
                         method.MethodKind == MethodKind.PropertyGet ||
                         method.MethodKind == MethodKind.PropertySet))
                        continue;

                    return true;
                }
            }
            return false;
        }
    }
}
