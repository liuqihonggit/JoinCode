using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace JccAuditCli.Tests;

/// <summary>
/// DiRegistrationExtractor 的单元测试 — 按 DI 注册模式分组
/// 每个测试验证一个具体的注册模式是否能被正确提取
/// </summary>
public sealed class DiRegistrationExtractorTests
{
    /// <summary>
    /// 辅助方法：创建带指定源码和 DI 引用的 Compilation，运行 Extract 并返回结果
    /// </summary>
    private static (List<ServiceRegistration>, List<ConstructorDependency>) ExtractFrom(
        string source,
        PortableExecutableReference[] extraRefs = null!)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var fileName = "ServiceRegistration.Test.cs";
        syntaxTree = syntaxTree.WithFilePath(fileName);

        var references = Basic.Reference.Assemblies.Net80.References.All
            .Cast<PortableExecutableReference>()
            .ToImmutableArray();

        if (extraRefs is { Length: > 0 })
        {
            references = references.AddRange(extraRefs);
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return DiRegistrationExtractor.Extract(compilation);
    }

    [Fact]
    public void Debug_DumpAllResults()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<IFoo, Foo>();
                    return services;
                }
            }
            public interface IFoo { }
            public class Foo : IFoo { }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();

        // 先检查找到了多少 InvocationExpressionSyntax
        var invocations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().ToList();
        var msgs = new List<string> { $"Found {invocations.Count} InvocationExpressionSyntax nodes:" };
        foreach (var inv in invocations)
        {
            var expr = inv.Expression;
            msgs.Add($"  Full: {inv}");
            msgs.Add($"  Expression type: {expr.GetType().Name}");
            msgs.Add($"  Expression: {expr}");

            if (expr is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax mae)
            {
                msgs.Add($"    Name: {mae.Name}");
                msgs.Add($"    Name type: {mae.Name.GetType().Name}");
                if (mae.Name is Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax gn)
                {
                    msgs.Add($"    TypeArgs: {string.Join(", ", gn.TypeArgumentList.Arguments)}");
                }
            }
        }

        var (regs, deps) = ExtractFrom(source);
        msgs.Add($"Extract result -> Regs: {regs.Count}, Deps: {deps.Count}");

        throw new Exception(string.Join("\n", msgs));
    }

    // ==================== 模式1: AddSingleton<TInterface, TImpl>() — 双泛型无工厂 ====================

    [Fact]
    public void AddSingleton_TwoTypeArgs_NoFactory_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<IFoo, Foo>();
                    return services;
                }
            }
            public interface IFoo { }
            public class Foo : IFoo { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("IFoo", regs[0].ServiceType);
        Assert.Equal("Foo", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
        Assert.Empty(deps);
    }

    // ==================== 模式2: AddSingleton<T>() — 单泛型无工厂 ====================

    [Fact]
    public void AddSingleton_SingleTypeArg_NoFactory_ShouldExtractSelfRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<Foo>();
                    return services;
                }
            }
            public class Foo { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("Foo", regs[0].ServiceType);
        Assert.Equal("Foo", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式3: AddSingleton<TInterface, TImpl>(sp => new TImpl()) — 双泛型+lambda工厂 ====================

    [Fact]
    public void AddSingleton_TwoTypeArgs_LambdaFactory_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<ILogger, FileLogger>(sp => new FileLogger());
                    return services;
                }
            }
            public interface ILogger { }
            public class FileLogger : ILogger { public FileLogger() { } }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("ILogger", regs[0].ServiceType);
        Assert.Equal("FileLogger", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式4: AddSingleton<T>(sp => new T(...)) — 单泛型+lambda工厂（无工厂参数） ====================

    [Fact]
    public void AddSingleton_SingleTypeArgs_LambdaFactory_NoDeps_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<Foo>(sp => new Foo());
                    return services;
                }
            }
            public class Foo { public Foo() { } }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("Foo", regs[0].ServiceType);
        Assert.Equal("Foo", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式5: AddSingleton<T>(sp => new T(sp.GetRequiredService<X>())) — lambda工厂+隐式依赖 ====================

    [Fact]
    public void AddSingleton_LambdaFactory_GetRequiredService_ShouldExtractImplicitDependency()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<Foo>(sp => new Foo(sp.GetRequiredService<Bar>()));
                    return services;
                }
            }
            public class Foo { public Foo(Bar bar) { } }
            public class Bar { }
            """;

        var (regs, deps) = ExtractFrom(source);

        // 应该提取 Foo → Bar 的隐式构造函数依赖
        Assert.NotEmpty(deps);
        var fooDep = deps.FirstOrDefault(d => d.ClassName == "Foo" && d.DependencyType == "Bar");
        Assert.NotNull(fooDep);
        Assert.False(fooDep.IsOptional);
    }

    // ==================== 模式6: AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>()) — 转型工厂 ====================

    [Fact]
    public void AddSingleton_LambdaFactory_GetRequiredService_Transform_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<ILogger>(sp => sp.GetRequiredService<FileLogger>());
                    return services;
                }
            }
            public interface ILogger { }
            public class FileLogger : ILogger { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("ILogger", regs[0].ServiceType);
        Assert.Equal("FileLogger", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式7: AddSingleton<TInterface>(sp => (TInterface)sp.GetRequiredService<TImpl>()) — 转型工厂 ====================

    [Fact]
    public void AddSingleton_CastFactory_GetRequiredService_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<ILogger>(sp => (ILogger)sp.GetRequiredService<FileLogger>());
                    return services;
                }
            }
            public interface ILogger { }
            public class FileLogger : ILogger { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("ILogger", regs[0].ServiceType);
        Assert.Equal("FileLogger", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式8: AddSingleton<T>(sp => new T { 复杂构造 }) — Block体lambda ====================

    [Fact]
    public void AddSingleton_BlockBodyLambda_ShouldExtractRegistration()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<Foo>(sp => {
                        return new Foo();
                    });
                    return services;
                }
            }
            public class Foo { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("Foo", regs[0].ServiceType);
        Assert.Equal("Foo", regs[0].ImplementationType);
    }

    // ==================== 模式9: AddSingleton<T>(sp => new T(复杂参数列表)) — Block体+GetRequiredService ====================

    [Fact]
    public void AddSingleton_BlockBodyLambda_GetRequiredService_ShouldExtractDependency()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<Foo>(sp => {
                        return new Foo(sp.GetRequiredService<Bar>());
                    });
                    return services;
                }
            }
            public class Foo { public Foo(Bar bar) { } }
            public class Bar { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.NotEmpty(deps);
        var fooDep = deps.FirstOrDefault(d => d.ClassName == "Foo" && d.DependencyType == "Bar");
        Assert.NotNull(fooDep);
    }

    // ==================== 模式10: AddScoped / AddTransient ====================

    [Fact]
    public void AddScoped_ShouldExtractScopedLifetime()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddScoped<IContext, AppContext>();
                    return services;
                }
            }
            public interface IContext { }
            public class AppContext : IContext { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("Scoped", regs[0].Lifetime);
    }

    [Fact]
    public void AddTransient_ShouldExtractTransientLifetime()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddTransient<IDisposable, TemporaryResource>();
                    return services;
                }
            }
            public interface IDisposable { void Dispose(); }
            public class TemporaryResource : IDisposable { public void Dispose() { } }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("Transient", regs[0].Lifetime);
    }

    // ==================== 模式11: AddHostedService<T> ====================

    [Fact]
    public void AddHostedService_ShouldExtractAsSingleton()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddHostedService<MyHostedService>();
                    return services;
                }
            }
            public class MyHostedService : BackgroundService
            {
                protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
            }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(regs);
        Assert.Equal("MyHostedService", regs[0].ImplementationType);
        Assert.Equal("Singleton", regs[0].Lifetime);
    }

    // ==================== 模式12: 复杂工厂 — new T(sp.GetRequiredService<X>(), sp.GetService<Y>()) ====================

    [Fact]
    public void AddSingleton_ComplexFactory_MultipleDeps_ShouldExtractAllDependencies()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<ComplexService>(sp =>
                        new ComplexService(
                            sp.GetRequiredService<DependencyA>(),
                            sp.GetService<DependencyB>()));
                    return services;
                }
            }
            public class ComplexService { public ComplexService(DependencyA a, DependencyB b) { } }
            public class DependencyA { }
            public class DependencyB { }
            """;

        var (regs, deps) = ExtractFrom(source);

        // 应该提取两个依赖
        Assert.Equal(2, deps.Count);
        Assert.Contains(deps, d => d.DependencyType == "DependencyA");
        Assert.Contains(deps, d => d.DependencyType == "DependencyB");
    }

    // ==================== 模式13: ILogger/IOptions 应该被跳过 ====================

    [Fact]
    public void AddSingleton_LoggerDep_ShouldBeSkipped()
    {
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Logging;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddMyServices(this IServiceCollection services)
                {
                    services.AddSingleton<MyService>(sp =>
                        new MyService(sp.GetService<ILogger<MyService>>()));
                    return services;
                }
            }
            public interface ILogger<T> { }
            public class MyService { public MyService(ILogger<MyService> logger) { } }
            """;

        var (regs, deps) = ExtractFrom(source);

        // ILogger 应该被跳过，不提取依赖
        Assert.Empty(deps);
    }

    // ==================== 模式14: 特性标记 [Register] ====================

    [Fact]
    public void RegisterAttribute_ShouldExtractRegistration()
    {
        var source = """
            using System;

            [JoinCode.Abstractions.Attributes.Register]
            public class MyService { }

            [JoinCode.Abstractions.Attributes.Register(typeof(IRepository))]
            public class Repository : IRepository { }

            namespace JoinCode.Abstractions.Attributes
            {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class RegisterAttribute : Attribute
                {
                    public Type InterfaceType { get; }
                    public RegisterAttribute() { }
                    public RegisterAttribute(Type interfaceType) => InterfaceType = interfaceType;
                }
            }
            public interface IRepository { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Equal(2, regs.Count);
        Assert.Contains(regs, r => r.ServiceType == "MyService" && r.ImplementationType == "MyService");
        Assert.Contains(regs, r => r.ServiceType == "IRepository" && r.ImplementationType == "Repository");
    }

    // ==================== 模式15: 构造函数参数分析（Class Declaration 级别） ====================

    [Fact]
    public void ClassWithConstructorDeps_ShouldExtractDependencies()
    {
        var source = """
            using System;

            public static class ServiceRegistration
            {
            }

            public class MyController
            {
                private readonly IDependencyA _depA;
                private readonly IDependencyB _depB;

                public MyController(IDependencyA depA, IDependencyB depB)
                {
                    _depA = depA;
                    _depB = depB;
                }
            }

            public interface IDependencyA { }
            public interface IDependencyB { }
            """;

        var (regs, deps) = ExtractFrom(source);

        // 应该提取两个构造函数依赖
        Assert.Equal(2, deps.Count);
        var myDeps = deps.Where(d => d.ClassName == "MyController").ToList();
        Assert.Equal(2, myDeps.Count);
        Assert.Contains(myDeps, d => d.DependencyType == "IDependencyA");
        Assert.Contains(myDeps, d => d.DependencyType == "IDependencyB");
    }

    // ==================== 模式16: 可选构造函数参数不应该被提取为硬依赖 ====================

    [Fact]
    public void ClassWithOptionalParam_ShouldNotExtractHardDependency()
    {
        var source = """
            using System;

            public static class ServiceRegistration
            {
            }

            public class MyController
            {
                private readonly IDependencyA _depA;
                private readonly IDependencyB _depB;

                public MyController(IDependencyA depA, IDependencyB depB = null)
                {
                    _depA = depA;
                    _depB = depB;
                }
            }

            public interface IDependencyA { }
            public interface IDependencyB { }
            """;

        var (regs, deps) = ExtractFrom(source);

        Assert.Single(deps);
        Assert.Equal("IDependencyA", deps[0].DependencyType);
        Assert.False(deps[0].IsOptional);

        // 第二个依赖应该是可选的
        // 实际上 ExtractConstructorDeps 对 IDependencyB 也会添加，但是 IsOptional=true
    }
}
