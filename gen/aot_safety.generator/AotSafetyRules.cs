namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AotSafetyRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleDictionaryObjectNullable = new(
            "JCC1001",
            "AOT incompatible: Dictionary<string, object?> is unsafe under NativeAOT",
            "Type '{0}' uses Dictionary<string, object?> which cannot be safely serialized under NativeAOT. Use Dictionary<string, JsonElement> or a strongly-typed alternative instead.",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "NativeAOT requires all serialized types to be determined at compile time. The object? value type cannot satisfy this requirement.");

        private static readonly DiagnosticDescriptor RuleDictionaryObjectNonNullable = new(
            "JCC1002",
            "AOT incompatible: Dictionary<string, object> is unsafe under NativeAOT",
            "Type '{0}' uses Dictionary<string, object> which cannot be safely serialized under NativeAOT. Use Dictionary<string, JsonElement> or a strongly-typed alternative instead.",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "NativeAOT requires all serialized types to be determined at compile time. The object value type cannot satisfy this requirement.");

        private static readonly DiagnosticDescriptor RuleInheritsDictionaryObject = new(
            "JCC1003",
            "AOT incompatible: Type inherits from Dictionary<string, object?>",
            "Type '{0}' inherits from Dictionary<string, object?> which cannot be safely serialized under NativeAOT. Use composition with Dictionary<string, JsonElement> or a strongly-typed wrapper instead.",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "Inheriting from Dictionary<string, object?> makes the entire type unsafe for AOT serialization.");

        private static readonly DiagnosticDescriptor RuleDynamicKeyword = new(
            "JCC1004",
            "AOT incompatible: dynamic 关键字在 NativeAOT 下不支持",
            "dynamic 关键字依赖运行时动态分发，NativeAOT 不支持。使用具体类型、JsonElement 或源码生成器替代。",
            "AotSafety",
            DiagnosticSeverity.Error,
            true,
            "dynamic 关键字依赖 DLR (Dynamic Language Runtime) 进行运行时方法解析. NativeAOT 编译时需要确定所有类型, DLR 的 CallSite 缓存和后期绑定机制无法在 AOT 环境中工作. 替代方案: 1) 使用具体类型; 2) 使用 JsonElement 处理动态 JSON; 3) 使用源码生成器在编译期生成代码.");

        private static readonly DiagnosticDescriptor RuleUsingInCsFile = new(
            "JCC1005",
            "代码规范: .cs 文件内禁止写 using 语句",
            "using 语句 '{0}' 应移动到 GlobalUsings.cs 统一管理，.cs 文件内禁止写 using。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "所有命名空间引用必须放在 GlobalUsings.cs 统一管理. 原因: 1) 避免重复引用; 2) 统一控制可见性; 3) 方便全局替换. 例外: GlobalUsings.cs 文件本身、生成的代码 (obj/ 目录).");

        private static readonly DiagnosticDescriptor RuleTooManyParameters = new(
            "JCC1006",
            "代码规范: 方法参数超过8个应封装为类",
            "方法 '{0}' 有 {1} 个参数，超过8个参数应封装为 Options/Request 类。",
            "CodeStyle",
            DiagnosticSeverity.Info,
            true,
            "参数过多降低可读性和可维护性. 正确做法: 1) 封装相关参数为 Options/Request/Config 类; 2) 使用构建器模式; 3) 使用记录类型 (record). 例外: 构造函数、override 方法、接口实现、测试方法、partial 方法.");

        private static readonly DiagnosticDescriptor RuleSwitchOnString = new(
            "JCC1010",
            "代码风格: switch 判断字符串应使用枚举+特性描述替代",
            "switch 语句判断 string 类型是魔法字符串模式。应定义枚举 + [EnumValue] 特性，用枚举 switch 替代字符串 switch，提高类型安全性和可维护性。",
            "CodeStyle",
            DiagnosticSeverity.Info,
            true,
            "String switch is a magic string pattern. Define an enum with [EnumValue] attributes and use enum switch instead for type safety and maintainability.");

        private static readonly DiagnosticDescriptor RuleReflectionEmit = new(
            "JCC1007",
            "AOT 不兼容: System.Reflection.Emit 在 NativeAOT 下不支持",
            "调用 '{0}' 属于 System.Reflection.Emit 命名空间，NativeAOT 不支持运行期 IL 生成。使用源码生成器在编译期生成代码替代。",
            "AotSafety",
            DiagnosticSeverity.Error,
            true,
            "System.Reflection.Emit 依赖运行期动态生成 IL 方法体, NativeAOT 编译时需要确定所有代码. 替代方案: 1) 使用 Roslyn 源码生成器 (IIncrementalGenerator); 2) 使用表达式树编译 (Expression.Compile) 在 AOT 下也不支持; 3) 改为静态代码生成.");

        private static readonly DiagnosticDescriptor RuleAssemblyLoad = new(
            "JCC1013",
            "AOT 风险: Assembly.Load 在 NativeAOT 下可能失败",
            "调用 '{0}' 在 NativeAOT 下可能因程序集被裁剪而失败。使用静态引用或 Trimmer 根配置替代。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "Assembly.Load/LoadFrom/Load(AssemblyName) 依赖运行期加载程序集, NativeAOT 裁剪后程序集可能不存在. 替代方案: 1) 使用静态 ProjectReference; 2) 在 csproj 配置 TrimmerRootDescription; 3) 使用 typeof(T).Assembly 获取已知程序集.");

        private static readonly DiagnosticDescriptor RuleTypeGetType = new(
            "JCC1014",
            "AOT 风险: Type.GetType(string) 在 NativeAOT 下可能返回 null",
            "调用 Type.GetType('{0}') 依赖运行期类型解析，NativeAOT 裁剪后目标类型可能被移除。使用 typeof(T) 编译期已知类型替代。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "Type.GetType(string) 依赖运行期类型名解析, NativeAOT 裁剪后非根类型可能被移除导致返回 null. 替代方案: 1) 使用 typeof(T) 编译期已知; 2) 使用源码生成器生成类型映射; 3) 在 TrimmerRootDescriptor 中保留所需类型.");

        private static readonly DiagnosticDescriptor RuleActivatorCreateInstance = new(
            "JCC1015",
            "AOT 风险: Activator.CreateInstance(Type) 在 NativeAOT 下性能差且可能失败",
            "调用 '{0}' 依赖运行期反射实例化，NativeAOT 下需要类型已注册。使用 new T() 或工厂模式替代。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "Activator.CreateInstance(Type) 依赖运行期反射构造, NativeAOT 下需要类型在 TrimmerRootDescriptor 中注册. 替代方案: 1) 使用 new T() 直接构造; 2) 使用工厂模式 + switch 分发; 3) 使用源码生成器生成工厂代码.");

        private static readonly DiagnosticDescriptor RuleMethodInfoInvoke = new(
            "JCC1016",
            "AOT 风险: MethodInfo.Invoke 在 NativeAOT 下性能差",
            "调用 '{0}.Invoke' 依赖运行期反射调用，NativeAOT 下性能远不如直接调用。使用直接调用或源码生成器分发替代。",
            "AotSafety",
            DiagnosticSeverity.Warning,
            true,
            "MethodInfo.Invoke 依赖运行期方法解析和参数装箱, NativeAOT 下性能远不如直接调用. 替代方案: 1) 使用委托 (Delegate) 或 Func/Action; 2) 使用源码生成器生成分发代码; 3) 使用接口多态替代反射调用.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                RuleDictionaryObjectNullable, RuleDictionaryObjectNonNullable, RuleInheritsDictionaryObject,
                RuleDynamicKeyword, RuleUsingInCsFile, RuleTooManyParameters, RuleSwitchOnString,
                RuleReflectionEmit, RuleAssemblyLoad, RuleTypeGetType, RuleActivatorCreateInstance, RuleMethodInfoInvoke);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeNode,
                SyntaxKind.ObjectCreationExpression,
                SyntaxKind.VariableDeclaration,
                SyntaxKind.PropertyDeclaration,
                SyntaxKind.FieldDeclaration,
                SyntaxKind.Parameter);
            context.RegisterSyntaxNodeAction(AnalyzeDynamicKeyword, SyntaxKind.IdentifierName);
            context.RegisterSyntaxNodeAction(AnalyzeUsingInCsFile, SyntaxKind.UsingDirective);
            context.RegisterSyntaxNodeAction(AnalyzeTooManyParameters, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeSwitchOnString, SyntaxKind.SwitchStatement);
            context.RegisterSyntaxNodeAction(AnalyzeReflectionApi, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            switch (ctx.Node)
            {
                case PropertyDeclarationSyntax prop:
                    CheckTypeSymbol(ctx, prop.Type, prop.Identifier.GetLocation());
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                        CheckTypeSymbol(ctx, field.Declaration.Type, v.Identifier.GetLocation());
                    break;
                case ParameterSyntax param:
                    CheckTypeSymbol(ctx, param.Type, param.Identifier.GetLocation());
                    break;
                case VariableDeclarationSyntax varDecl:
                    foreach (var v in varDecl.Variables)
                        CheckTypeSymbol(ctx, varDecl.Type, v.Identifier.GetLocation());
                    break;
                case ObjectCreationExpressionSyntax obj:
                    CheckTypeSymbol(ctx, obj.Type, obj.Type.GetLocation());
                    break;
            }
        }

        private static void CheckTypeSymbol(SyntaxNodeAnalysisContext ctx, TypeSyntax? typeSyntax, Location location)
        {
            if (typeSyntax is null) return;

            var symbol = ctx.SemanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
            if (symbol is null) return;

            if (IsDictionaryStringObject(symbol))
            {
                var isNullable = symbol.TypeArguments.Length >= 2 &&
                    symbol.TypeArguments[1].IsReferenceType;

                var rule = isNullable ? RuleDictionaryObjectNullable : RuleDictionaryObjectNonNullable;
                var displayStr = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                ctx.ReportDiagnostic(Diagnostic.Create(rule, location, displayStr));
            }

            if (symbol.BaseType is not null && IsDictionaryStringObject(symbol.BaseType))
            {
                var displayStr = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                ctx.ReportDiagnostic(Diagnostic.Create(RuleInheritsDictionaryObject, location, displayStr));
            }
        }

        private static bool IsDictionaryStringObject(INamedTypeSymbol type)
        {
            if (!type.IsGenericType) return false;

            var def = type.ConstructedFrom;
            if (def is null) return false;

            var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
            if (fullName != "System.Collections.Generic.Dictionary") return false;

            if (type.TypeArguments.Length != 2) return false;

            if (type.TypeArguments[0].SpecialType != SpecialType.System_String) return false;
            if (type.TypeArguments[1].SpecialType != SpecialType.System_Object) return false;

            return true;
        }

        /// <summary>
        /// JCC1004: 禁止使用 dynamic 关键字
        /// </summary>
        private static void AnalyzeDynamicKeyword(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not IdentifierNameSyntax identifier) return;
            if (identifier.Identifier.ValueText != "dynamic") return;

            var parent = identifier.Parent;
            if (parent is null) return;

            if (parent is VariableDeclarationSyntax ||
                parent is ParameterSyntax ||
                parent is TypeArgumentListSyntax ||
                parent is GenericNameSyntax ||
                parent is PredefinedTypeSyntax ||
                parent is ArrayTypeSyntax ||
                parent is NullableTypeSyntax ||
                parent is CastExpressionSyntax ||
                parent is ObjectCreationExpressionSyntax ||
                parent is TypeOfExpressionSyntax ||
                parent is DefaultExpressionSyntax ||
                parent is SizeOfExpressionSyntax ||
                parent is PointerTypeSyntax ||
                parent is FunctionPointerParameterSyntax ||
                parent is DeclarationPatternSyntax ||
                parent is RecursivePatternSyntax)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDynamicKeyword, identifier.GetLocation()));
                return;
            }

            if (parent is MethodDeclarationSyntax methodDecl && methodDecl.ReturnType == identifier)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDynamicKeyword, identifier.GetLocation()));
                return;
            }

            if (parent is VariableDeclarationSyntax varDecl && varDecl.Type == identifier)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDynamicKeyword, identifier.GetLocation()));
            }
        }

        /// <summary>
        /// JCC1005: 禁止在.cs文件内写using语句
        /// </summary>
        private static void AnalyzeUsingInCsFile(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not UsingDirectiveSyntax usingDirective) return;

            var filePath = usingDirective.SyntaxTree.FilePath;

            if (string.IsNullOrEmpty(filePath)) return;
            if (filePath[0] == '/') return;

            if (filePath.EndsWith("GlobalUsings.cs", StringComparison.Ordinal)) return;

            if (filePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                filePath.Contains("/obj/", StringComparison.Ordinal)) return;

            if (filePath.Contains("AotSafety.Generator", StringComparison.Ordinal) ||
                filePath.Contains("CodeFixes", StringComparison.Ordinal) ||
                filePath.Contains("EnumMetadata.Generator", StringComparison.Ordinal) ||
                filePath.Contains("McpToolDispatch.Generator", StringComparison.Ordinal)) return;

            if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)) return;

            var usingName = usingDirective.Name?.ToString() ?? "?";
            ctx.ReportDiagnostic(Diagnostic.Create(RuleUsingInCsFile, usingDirective.GetLocation(), usingName));
        }

        /// <summary>
        /// JCC1006: 方法参数超过8个应封装为类
        /// </summary>
        private static void AnalyzeTooManyParameters(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not MethodDeclarationSyntax methodDecl) return;

            var paramCount = methodDecl.ParameterList.Parameters.Count;
            if (paramCount <= 8) return;

            if (methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;

            if (methodDecl.ExplicitInterfaceSpecifier is not null) return;

            if (methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) return;

            if (AotSafetyHelpers.IsInsideTestMethod(methodDecl)) return;

            var methodName = methodDecl.Identifier.ValueText;
            if (methodName == "Main") return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleTooManyParameters,
                methodDecl.Identifier.GetLocation(), methodName, paramCount));
        }

        /// <summary>
        /// JCC1010: switch 判断字符串应推荐枚举+特性描述
        /// </summary>
        private static void AnalyzeSwitchOnString(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not SwitchStatementSyntax switchStmt) return;

            var switchExprType = ctx.SemanticModel.GetTypeInfo(switchStmt.Expression, ctx.CancellationToken).Type;
            if (switchExprType is null) return;
            if (switchExprType.SpecialType != SpecialType.System_String) return;

            var caseCount = switchStmt.Sections.Sum(s => s.Labels.Count(l => l is CaseSwitchLabelSyntax));
            if (caseCount <= 2) return;

            ctx.ReportDiagnostic(Diagnostic.Create(RuleSwitchOnString, switchStmt.Expression.GetLocation()));
        }

        /// <summary>
        /// JCC1007/1013-1016: 检测 AOT 不兼容的反射 API 调用
        /// </summary>
        private static void AnalyzeReflectionApi(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not InvocationExpressionSyntax invocation) return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return;

            var containingType = methodSymbol.ContainingType;
            if (containingType is null) return;

            var containingNamespace = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var typeName = containingType.Name;
            var methodName = methodSymbol.Name;
            var fullName = $"{containingNamespace}.{typeName}.{methodName}";

            // JCC1007: System.Reflection.Emit.* — Error
            if (containingNamespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleReflectionEmit, invocation.GetLocation(), fullName));
                return;
            }

            // JCC1013: Assembly.Load/LoadFrom/LoadFile — Warning
            if (typeName == "Assembly" && containingNamespace == "System.Reflection" &&
                (methodName == "Load" || methodName == "LoadFrom" || methodName == "LoadFile" ||
                 methodName == "LoadWithPartialName" || methodName == "ReflectionOnlyLoad" ||
                 methodName == "ReflectionOnlyLoadFrom"))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleAssemblyLoad, invocation.GetLocation(), fullName));
                return;
            }

            // JCC1014: Type.GetType(string) — Warning
            if (typeName == "Type" && containingNamespace == "System" && methodName == "GetType")
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleTypeGetType, invocation.GetLocation(), methodName));
                return;
            }

            // JCC1015: Activator.CreateInstance(Type) — Warning
            if (typeName == "Activator" && containingNamespace == "System" &&
                (methodName == "CreateInstance" || methodName == "CreateInstanceFrom"))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleActivatorCreateInstance, invocation.GetLocation(), fullName));
                return;
            }

            // JCC1016: MethodInfo.Invoke — Warning
            if (typeName == "MethodInfo" && containingNamespace == "System.Reflection" && methodName == "Invoke")
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleMethodInfoInvoke, invocation.GetLocation(), typeName));
            }
        }
    }
}
