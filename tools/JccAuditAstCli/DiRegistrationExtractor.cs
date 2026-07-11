using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JccAuditCli;

/// <summary>
/// DI 注册信息提取器：从 Compilation 中提取服务注册和构造函数依赖
/// 覆盖四种注册模式：
///  1. AddSingleton&lt;TInterface, TImpl&gt;() — 双泛型
///  2. AddSingleton&lt;T&gt;() — 单泛型（无工厂）
///  3. AddSingleton&lt;T&gt;(sp =&gt; ...) — Lambda 工厂 + GetRequiredService 隐式依赖
///  4. [Register] 特性 — 特性标记
/// </summary>
public static class DiRegistrationExtractor
{
    public static (List<ServiceRegistration> Registrations, List<ConstructorDependency> Dependencies) Extract(Compilation compilation)
    {
        var registrations = new List<ServiceRegistration>();
        var dependencies = new List<ConstructorDependency>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var filePath = syntaxTree.FilePath ?? string.Empty;
            if (filePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                filePath.Contains("/obj/", StringComparison.Ordinal))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!IsDiRelatedFile(fileName))
                continue;

            var root = syntaxTree.GetRoot();
            ExtractLambdaFactoryDeps(root, filePath, registrations, dependencies);
            ExtractRegistrations(root, filePath, registrations);
            ExtractConstructorDeps(syntaxTree, compilation, dependencies);
        }

        return (registrations, dependencies);
    }

    private static bool IsDiRelatedFile(string fileName)
    {
        return fileName.StartsWith("ServiceRegistration", StringComparison.Ordinal) ||
               fileName.StartsWith("DependencyInjection", StringComparison.Ordinal) ||
               fileName.StartsWith("ServiceExtensions", StringComparison.Ordinal) ||
               fileName.StartsWith("Registration", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 Lambda 工厂中提取隐式依赖：services.AddSingleton&lt;T&gt;(sp =&gt; new T(sp.GetRequiredService&lt;X&gt;(), ...))
    /// 遍历 lambda 工厂体中的 GetRequiredService/GetService 调用，提取依赖类型并注册为隐式 ServiceRegistration
    /// </summary>
    private static void ExtractLambdaFactoryDeps(SyntaxNode root, string filePath, List<ServiceRegistration> registrations, List<ConstructorDependency> dependencies)
    {
        // 1) 提取方法调用工厂中的 lambda 工厂依赖
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var args = invocation.ArgumentList?.Arguments;
            if (args is null || args.Value.Count < 2) continue;

            var secondArg = args.Value[1];
            // Lambda 工厂: sp => ...
            if (secondArg.Expression is LambdaExpressionSyntax lambda)
            {
                ExtractDepsFromLambda(lambda, filePath, registrations, dependencies);
            }
            // ParenthesizedLambdaExpression (显式括号)
            if (secondArg.Expression is ParenthesizedLambdaExpressionSyntax parenLambda)
            {
                ExtractDepsFromLambda(parenLambda, filePath, registrations, dependencies);
            }
        }
    }

    /// <summary>
    /// 从 lambda 工厂体中提取 GetRequiredService&lt;T&gt;/GetService&lt;T&gt; 调用作为依赖
    /// 同时处理 ObjectCreationExpression 中的参数列表
    /// </summary>
    private static void ExtractDepsFromLambda(LambdaExpressionSyntax lambda, string filePath,
        List<ServiceRegistration> registrations, List<ConstructorDependency> dependencies)
    {
        // 处理 ExpressionBody: sp => new T(...)
        if (lambda.ExpressionBody is ExpressionSyntax body)
        {
            ExtractDepsFromExpression(body, lambda, filePath, registrations, dependencies);
            return;
        }

        // 处理 Block body: sp => { return new T(...); }
        if (lambda.Body is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt is ReturnStatementSyntax retStmt && retStmt.Expression is not null)
                {
                    ExtractDepsFromExpression(retStmt.Expression, lambda, filePath, registrations, dependencies);
                }
            }
        }
    }

    /// <summary>
    /// 从表达式中提取 sp.GetRequiredService&lt;T&gt;() 调用和 new T(...) 中的构造函数依赖
    /// </summary>
    private static void ExtractDepsFromExpression(ExpressionSyntax expr, LambdaExpressionSyntax lambda,
        string filePath, List<ServiceRegistration> registrations, List<ConstructorDependency> dependencies)
    {
        // 处理 ObjectCreationExpression: new SomeService(...)
        if (expr is ObjectCreationExpressionSyntax objCreation)
        {
            // 提取构造函数参数中的 GetRequiredService 调用
            if (objCreation.ArgumentList?.Arguments is { Count: > 0 })
            {
                foreach (var arg in objCreation.ArgumentList.Arguments)
                {
                    ExtractServiceCalls(arg.Expression, lambda, filePath, registrations, dependencies);
                }
            }
            return;
        }

        // CastExpression: (T)sp.GetRequiredService<X>()
        if (expr is CastExpressionSyntax castExpr)
        {
            ExtractServiceCalls(castExpr.Expression, lambda, filePath, registrations, dependencies);
            return;
        }

        // Direct service call
        ExtractServiceCalls(expr, lambda, filePath, registrations, dependencies);
    }

    /// <summary>
    /// 递归查找表达式中所有的 GetRequiredService&lt;T&gt;/GetService&lt;T&gt; 调用
    /// </summary>
    private static void ExtractServiceCalls(ExpressionSyntax expr, LambdaExpressionSyntax lambda,
        string filePath, List<ServiceRegistration> registrations, List<ConstructorDependency> dependencies)
    {
        foreach (var inv in expr.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = ExtractMemberAccessName(inv);
            if (methodName is "GetRequiredService" or "GetService")
            {
                var typeArgs = FindTypeArguments(inv);
                if (typeArgs is { Count: 1 })
                {
                    var depType = typeArgs[0].ToString();
                    // 跳过基础设施类型
                    if (depType == "IServiceProvider" ||
                        depType.StartsWith("ILogger", StringComparison.Ordinal) ||
                        depType.StartsWith("IOptions", StringComparison.Ordinal))
                        continue;

                    // 注册隐式依赖
                    // 从 lambda 的返回类型推断服务类型
                    var serviceType = InferServiceTypeFromLambda(lambda, inv);
                    if (!string.IsNullOrEmpty(serviceType))
                    {
                        // 注册隐式服务到自身的映射（lambda 工厂返回的类型）
                        var returnType = InferLambdaReturnType(lambda);
                        if (!string.IsNullOrEmpty(returnType))
                        {
                            registrations.Add(new ServiceRegistration(serviceType, returnType, "Singleton"));
                        }

                        // 添加构造函数依赖
                        var lineInfo = inv.GetLocation().GetLineSpan();
                        dependencies.Add(new ConstructorDependency(
                            serviceType,
                            depType,
                            filePath,
                            lineInfo.StartLinePosition.Line + 1,
                            false));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从 lambda 工厂返回类型推断服务类型
    /// 例: sp => new Foo() → "Foo",  sp => (IFoo)new Foo() → "IFoo"
    /// </summary>
    private static string? InferLambdaReturnType(LambdaExpressionSyntax lambda)
    {
        if (lambda.ExpressionBody is ObjectCreationExpressionSyntax obj)
        {
            return obj.Type?.ToString();
        }
        if (lambda.ExpressionBody is CastExpressionSyntax cast && cast.Expression is ObjectCreationExpressionSyntax castObj)
        {
            return cast.Type.ToString(); // 接口类型
        }
        if (lambda.Body is BlockSyntax block)
        {
            foreach (var stmt in block.Statements.OfType<ReturnStatementSyntax>())
            {
                if (stmt.Expression is ObjectCreationExpressionSyntax retObj)
                {
                    return retObj.Type?.ToString();
                }
                // (T)new U() 转型
                if (stmt.Expression is CastExpressionSyntax retCast)
                {
                    return retCast.Type.ToString();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 从 lambda 工厂上下文推断当前注册的服务类型
    /// 通过向上查找父节点找到 AddSingleton&lt;T&gt;(sp => ...) 中的 T
    /// </summary>
    private static string? InferServiceTypeFromLambda(LambdaExpressionSyntax lambda, InvocationExpressionSyntax contextInv)
    {
        // 向上查找最近的 AddSingleton/AddScoped/AddTransient 调用
        var current = lambda.Parent;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax parentInv)
            {
                var methodName = ExtractMethodName(parentInv.Expression);
                if (methodName is "AddSingleton" or "AddScoped" or "AddTransient")
                {
                    var typeArgs = GetGenericArguments(parentInv);
                    if (typeArgs is { Count: 1 })
                    {
                        // 单泛型: AddSingleton<T>(sp => new TImpl(...))
                        // 需要推断实现类型
                        return typeArgs[0].ToString();
                    }
                    if (typeArgs is { Count: 2 })
                    {
                        // 双泛型: AddSingleton<TInterface, TImpl>(sp => ...)
                        // 第二个参数是实际实现
                        return typeArgs[1].ToString();
                    }
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private static void ExtractRegistrations(SyntaxNode root, string filePath, List<ServiceRegistration> registrations)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            ExtractRegistrationFromInvocation(invocation, filePath, registrations);
        }
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.AttributeLists.Count > 0))
        {
            ExtractAttributeBasedRegistration(classDecl, filePath, registrations);
        }
    }

    /// <summary>
    /// 提取 AddSingleton&lt;IFoo, Foo&gt;(sp => new Foo()) 的泛型参数
    /// invocation.Expression 可能是:
    ///   1. MemberAccessExpressionSyntax: services.AddSingleton&lt;IFoo, Foo&gt; → Name 是 GenericNameSyntax
    ///   2. GenericNameSyntax: 直接调用泛型扩展方法时
    /// </summary>
    private static List<TypeSyntax>? GetGenericArguments(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;

        // 模式1: services.AddSingleton&lt;IFoo, Foo&gt;  →  MAE where Name 是 GenericNameSyntax
        if (expr is MemberAccessExpressionSyntax mae && mae.Name is GenericNameSyntax gName)
            return gName.TypeArgumentList?.Arguments.ToList();

        // 模式2: 直接泛型名称
        if (expr is GenericNameSyntax gn)
            return gn.TypeArgumentList?.Arguments.ToList();

        return null;
    }

    private static void ExtractRegistrationFromInvocation(InvocationExpressionSyntax invocation, string filePath, List<ServiceRegistration> registrations)
    {
        var methodCall = invocation.Expression;
        var methodName = ExtractMethodName(methodCall);
        if (string.IsNullOrEmpty(methodName))
            return;

        var lifetime = ExtractLifetime(methodName);
        if (lifetime == null)
            return;

        var typeArgs = GetGenericArguments(invocation);
        if (typeArgs is { Count: >= 1 })
        {
            if (typeArgs.Count == 2)
            {
                registrations.Add(new ServiceRegistration(typeArgs[0].ToString(), typeArgs[1].ToString(), lifetime));
                return;
            }
            if (typeArgs.Count == 1)
            {
                var firstArg = typeArgs[0].ToString();
                var arguments = invocation.ArgumentList?.Arguments.ToList();
                if (arguments is { Count: >= 1 } && arguments[0].Expression is LambdaExpressionSyntax lambda)
                {
                    ExtractFactoryRegistration(firstArg, lambda, lifetime, registrations);
                    return;
                }
                registrations.Add(new ServiceRegistration(firstArg, firstArg, lifetime));
            }
        }
    }

    private static void ExtractFactoryRegistration(string serviceType, LambdaExpressionSyntax lambda, string lifetime, List<ServiceRegistration> registrations)
    {
        var body = lambda.Body;
        if (body is InvocationExpressionSyntax inv)
        {
            if (ExtractMemberAccessName(inv) is "GetRequiredService" or "GetService")
            {
                var typeArgs = FindTypeArguments(inv);
                if (typeArgs is { Count: 1 })
                {
                    registrations.Add(new ServiceRegistration(serviceType, typeArgs[0].ToString(), lifetime));
                    return;
                }
            }
        }
        if (body is CastExpressionSyntax cast)
        {
            var innerInv = cast.Expression as InvocationExpressionSyntax;
            if (innerInv is not null && ExtractMemberAccessName(innerInv) is "GetRequiredService" or "GetService")
            {
                var typeArgs = FindTypeArguments(innerInv);
                if (typeArgs is { Count: 1 })
                    registrations.Add(new ServiceRegistration(cast.Type.ToString(), typeArgs[0].ToString(), lifetime));
                return;
            }
        }
        if (body is ObjectCreationExpressionSyntax obj)
        {
            var implType = obj.Type?.ToString();
            if (!string.IsNullOrEmpty(implType))
            {
                registrations.Add(new ServiceRegistration(serviceType, implType, lifetime));
                return;
            }
        }
        if (body is BlockSyntax block)
        {
            foreach (var stmt in block.Statements.OfType<ReturnStatementSyntax>())
            {
                if (stmt.Expression is ObjectCreationExpressionSyntax retObj)
                {
                    var implType = retObj.Type?.ToString();
                    if (!string.IsNullOrEmpty(implType))
                    {
                        registrations.Add(new ServiceRegistration(serviceType, implType, lifetime));
                        ExtractFactoryDependencies(stmt, registrations);
                        return;
                    }
                }
            }
        }
    }

    private static List<TypeSyntax>? FindTypeArguments(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;
        if (expr is MemberAccessExpressionSyntax mae && mae.Name is GenericNameSyntax gName)
            return gName.TypeArgumentList?.Arguments.ToList();
        if (expr is GenericNameSyntax gn)
            return gn.TypeArgumentList?.Arguments.ToList();
        return null;
    }

    private static void ExtractFactoryDependencies(StatementSyntax statement, List<ServiceRegistration> registrations)
    {
        foreach (var inv in statement.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsServiceProviderCall))
        {
            var methodName = ExtractMemberAccessName(inv);
            if (methodName is "GetRequiredService" or "GetService")
            {
                var typeArgs = FindTypeArguments(inv);
                if (typeArgs is { Count: 1 })
                {
                    var depType = typeArgs[0].ToString();
                    if (depType == "IServiceProvider" || depType.StartsWith("ILogger", StringComparison.Ordinal) || depType.StartsWith("IOptions", StringComparison.Ordinal))
                        continue;
                    registrations.Add(new ServiceRegistration(depType, depType, "Singleton"));
                }
            }
        }
    }

    private static bool IsServiceProviderCall(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax mae)
            return mae.Expression?.ToString() == "sp";
        return false;
    }

    private static void ExtractAttributeBasedRegistration(ClassDeclarationSyntax classDecl, string filePath, List<ServiceRegistration> registrations)
    {
        var className = classDecl.Identifier.ToString();
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                string? lifetime = attrName switch
                {
                    "Register" => "Singleton",
                    _ => null
                };
                if (lifetime == null)
                    continue;

                var args = attr.ArgumentList?.Arguments.ToList();
                var lifetimeArg = args?.FirstOrDefault(a => a.NameColon?.Name.Identifier.ToString() == "Lifetime");
                if (lifetimeArg is not null)
                {
                    var lifetimeValue = lifetimeArg.Expression.ToString();
                    lifetime = lifetimeValue switch
                    {
                        "ServiceLifetime.Scoped" => "Scoped",
                        "ServiceLifetime.Transient" => "Transient",
                        _ => "Singleton"
                    };
                }

                var typeArgs = args?.Where(a => a.NameColon == null || a.NameColon?.Name.Identifier.ToString() != "Lifetime").ToList();
                if (typeArgs is { Count: >= 1 })
                {
                    registrations.Add(new ServiceRegistration(typeArgs[0].Expression.ToString(), className, lifetime));
                }
                else if (classDecl.BaseList?.Types.Count > 0)
                {
                    registrations.Add(new ServiceRegistration(classDecl.BaseList.Types[0].ToString(), className, lifetime));
                }
                else
                {
                    registrations.Add(new ServiceRegistration(className, className, lifetime));
                }
            }
        }
    }

    private static void ExtractConstructorDeps(SyntaxTree syntaxTree, Compilation compilation, List<ConstructorDependency> dependencies)
    {
        var filePath = syntaxTree.FilePath ?? string.Empty;
        foreach (var classDecl in syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => !c.Modifiers.Any(m => m.Text is "interface" or "abstract")))
        {
            var className = classDecl.Identifier.ToString();
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol is null || classSymbol.TypeKind == TypeKind.Interface || classSymbol.IsAbstract)
                continue;

            var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ShouldSkipClass(className, ns))
                continue;

            foreach (var ctor in classDecl.Members.OfType<ConstructorDeclarationSyntax>())
            {
                var parameters = ctor.ParameterList?.Parameters;
                if (parameters is null)
                    continue;
                foreach (var param in parameters)
                {
                    var paramType = param.Type?.ToString();
                    if (string.IsNullOrEmpty(paramType) || IsInfrastructureType(paramType))
                        continue;
                    var isOptional = param.Default is not null || param.Type is NullableTypeSyntax;
                    dependencies.Add(new ConstructorDependency(className, paramType, filePath, param.GetLocation().GetLineSpan().StartLinePosition.Line + 1, isOptional));
                }
            }
        }
    }

    private static bool ShouldSkipClass(string className, string ns)
    {
        return className is "Program" or "Startup" or "Configuration" or "Options" or "Config"
               || className.EndsWith("Attribute", StringComparison.Ordinal);
    }

    private static bool IsInfrastructureType(string type)
    {
        return type == "IServiceProvider"
               || type.StartsWith("ILogger", StringComparison.Ordinal)
               || type.StartsWith("IOptions", StringComparison.Ordinal)
               || type is "IConfiguration" or "IConfigurationSection" or "IWebHostEnvironment" or "IHostEnvironment" or "IServiceScopeFactory";
    }

    private static string? ExtractMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text,
            GenericNameSyntax gne => gne.Identifier.Text,
            _ => null
        };
    }

    private static string? ExtractMemberAccessName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text,
            _ => null
        };
    }

    private static string? ExtractLifetime(string methodName)
    {
        var baseName = methodName.StartsWith("Try", StringComparison.Ordinal)
            ? methodName.Substring(3)
            : methodName;
        return baseName switch
        {
            "AddSingleton" => "Singleton",
            "AddScoped" => "Scoped",
            "AddTransient" => "Transient",
            "AddHostedService" => "Singleton",
            _ => null
        };
    }
}
