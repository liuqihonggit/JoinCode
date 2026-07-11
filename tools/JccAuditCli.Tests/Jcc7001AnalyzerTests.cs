namespace JccAuditCli.Tests;

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using AotSafety.Generator;

/// <summary>
/// JCC7001 分析器的单元测试
/// 验证：lambda 内的方法调用、泛型方法调用、方法组引用等场景
/// </summary>
public sealed class Jcc7001AnalyzerTests
{
    private static readonly DiagnosticAnalyzer Analyzer = new AotSafetyAnalyzer();

    private static async Task<List<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var compilation = await CreateCompilationAsync(source);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(Analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        return diagnostics.Where(d => d.Id == "JCC7001").ToList();
    }

    private static async Task<Compilation> CreateCompilationAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = Basic.Reference.Assemblies.Net80.References.All
            .Cast<PortableExecutableReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 确保编译本身没有错误
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            var msg = string.Join("\n", errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"测试源码有编译错误:\n{msg}");
        }

        return compilation;
    }

    /// <summary>
    /// 验证：lambda 内调用 private static 方法，不应报 JCC7001
    /// </summary>
    [Fact]
    public async Task LambdaCall_PrivateStaticMethod_ShouldNotReport()
    {
        var source = """
            using System;
            using System.Collections.Generic;

            public static class DiExtensions
            {
                public static void AddServices(IServiceCollection services)
                {
                    services.AddSingleton(sp =>
                    {
                        var config = CreateDefaultConfig(sp);
                        return new MyService(config);
                    });
                }

                private static MyConfig CreateDefaultConfig(IServiceProvider sp)
                {
                    return new MyConfig();
                }
            }

            public interface IServiceCollection { void AddSingleton(Func<IServiceProvider, object> factory); }
            public interface IServiceProvider { }
            public sealed class MyConfig { }
            public sealed class MyService { public MyService(MyConfig config) { } }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().NotContain(d => d.GetMessage().Contains("CreateDefaultConfig"),
            "lambda 内调用的 private static 方法不应报为未引用");
    }

    /// <summary>
    /// 验证：Configure lambda 内调用 private static 方法（模拟 DI 注册场景）
    /// </summary>
    [Fact]
    public async Task ConfigureLambdaCall_PrivateStaticMethod_ShouldNotReport()
    {
        var source = """
            using System;
            using System.Collections.Generic;

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddPromptServices(this IServiceCollection services)
                {
                    services.AddSingleton<MyService>(sp =>
                    {
                        var configs = CreateDefaultReminderConfigs(sp);
                        return new MyService(configs);
                    });
                    return services;
                }

                private static List<string> CreateDefaultReminderConfigs(IServiceProvider sp)
                {
                    return [ "config1", "config2" ];
                }
            }

            public interface IServiceCollection
            {
                IServiceCollection AddSingleton<T>(Func<IServiceProvider, T> factory);
            }
            public interface IServiceProvider { object? GetService(Type type); }
            public sealed class MyService { public MyService(List<string> configs) { } }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().NotContain(d => d.GetMessage().Contains("CreateDefaultReminderConfigs"),
            "DI 注册 lambda 内调用的 private static 方法不应报为未引用");
    }

    /// <summary>
    /// 验证：extension method 中 Configure lambda 内调用 private static 方法
    /// 这是 Brain 项目中 CreateDefaultReminderConfigs 的精确复现
    /// </summary>
    [Fact]
    public async Task ExtensionMethod_ConfigureLambda_PrivateStaticMethod_ShouldNotReport()
    {
        var source = """
            using System;
            using System.Collections.Generic;

            public static class PromptsDependencyInjectionExtensions
            {
                public static IServiceCollection AddPromptServices(this IServiceCollection services)
                {
                    services.AddSingleton<MyService>(sp =>
                    {
                        var configs = CreateDefaultReminderConfigs(sp);
                        return new MyService(configs);
                    });
                    return services;
                }

                private static List<string> CreateDefaultReminderConfigs(IServiceProvider sp)
                {
                    return ["config1"];
                }
            }

            public interface IServiceCollection
            {
                IServiceCollection AddSingleton<T>(Func<IServiceProvider, T> factory);
            }
            public interface IServiceProvider { }
            public sealed class MyService { public MyService(List<string> configs) { } }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().NotContain(d => d.GetMessage().Contains("CreateDefaultReminderConfigs"),
            "extension method 中 Configure lambda 内调用的 private static 方法不应报为未引用");
    }

    /// <summary>
    /// 验证：泛型方法调用，不应报 JCC7001
    /// </summary>
    [Fact]
    public async Task GenericMethodCall_ShouldNotReport()
    {
        var source = """
            public sealed class Validator
            {
                public string? Validate()
                {
                    var error1 = ValidateItem(new StringItem { Value = "" });
                    var error2 = ValidateItem(new IntItem { Value = 0 });
                    return error1 ?? error2;
                }

                private static string? ValidateItem<T>(T item) where T : IValidatable
                {
                    return item.IsValid() ? null : "invalid";
                }
            }

            public interface IValidatable { bool IsValid(); }
            public sealed class StringItem : IValidatable { public string Value { get; set; } = ""; public bool IsValid() => !string.IsNullOrEmpty(Value); }
            public sealed class IntItem : IValidatable { public int Value { get; set; } public bool IsValid() => Value > 0; }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().NotContain(d => d.GetMessage().Contains("ValidateItem"),
            "泛型方法通过类型推断调用时不应报为未引用");
    }

    /// <summary>
    /// 验证：方法组引用（如 .Where(IsPathLike)），不应报 JCC7001
    /// </summary>
    [Fact]
    public async Task MethodGroupReference_ShouldNotReport()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public sealed class PathChecker
            {
                public List<string> FilterPaths(List<string> paths)
                {
                    return paths.Where(IsPathLike).ToList();
                }

                private static bool IsPathLike(string s)
                {
                    return s.Contains('/') || s.Contains('\\');
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().NotContain(d => d.GetMessage().Contains("IsPathLike"),
            "方法组引用的 private static 方法不应报为未引用");
    }

    /// <summary>
    /// 验证：真正未引用的 private 方法，应报 JCC7001
    /// </summary>
    [Fact]
    public async Task TrulyUnreferencedMethod_ShouldReport()
    {
        var source = """
            public sealed class DeadCode
            {
                public void UsedMethod() { }

                private static void NeverCalled()
                {
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("NeverCalled"),
            "真正未引用的 private 方法应报为未引用");
    }
}
