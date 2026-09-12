using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace JccAuditCli.Tests;

/// <summary>
/// ConstructorParamCounter 的单元测试 — 验证胖构造函数检测逻辑
/// </summary>
public sealed class ConstructorParamCounterTests
{
    /// <summary>
    /// 辅助方法：从源码创建 Compilation 并运行 Extract
    /// </summary>
    private static List<ConstructorParamInfo> ExtractFrom(string source, int threshold = ConstructorParamCounter.DefaultThreshold)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        syntaxTree = syntaxTree.WithFilePath("TestSource.cs");

        var references = Basic.Reference.Assemblies.Net80.References.All
            .Cast<PortableExecutableReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return ConstructorParamCounter.Extract(compilation, threshold);
    }

    [Fact]
    public void Extract_ClassWith9Parameters_FoundWhenThresholdIs8()
    {
        var source = """
            public class FatService
            {
                public FatService(int a, int b, int c, int d, int e, int f, int g, int h, int i) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().ContainSingle();
        results[0].ClassName.Should().Be("FatService");
        results[0].ParameterCount.Should().Be(9);
        results[0].ParameterTypes.Should().HaveCount(9);
    }

    [Fact]
    public void Extract_ClassWith8Parameters_NotFoundWhenThresholdIs8()
    {
        var source = """
            public class BorderlineService
            {
                public BorderlineService(int a, int b, int c, int d, int e, int f, int g, int h) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Extract_InfrastructureTypesFiltered()
    {
        var source = """
            using Microsoft.Extensions.Logging;
            using Microsoft.Extensions.Options;

            public class ServiceWithInfra
            {
                public ServiceWithInfra(
                    ILogger<ServiceWithInfra> logger,
                    IOptions<MyOptions> options,
                    int a, int b, int c, int d, int e, int f, int g, int h, int i)
                { }
            }
            public class MyOptions { }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().ContainSingle();
        // 11 个参数 - 2 个基础设施类型 = 9 个有效参数
        results[0].ParameterCount.Should().Be(9);
        results[0].ParameterTypes.Should().NotContain(t => t.StartsWith("ILogger", StringComparison.Ordinal));
        results[0].ParameterTypes.Should().NotContain(t => t.StartsWith("IOptions", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_MultipleConstructors_PicksFattest()
    {
        var source = """
            public class MultiCtorService
            {
                public MultiCtorService(int a) { }
                public MultiCtorService(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j) { }
                public MultiCtorService(int a, int b) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().ContainSingle();
        results[0].ParameterCount.Should().Be(10);
    }

    [Fact]
    public void Extract_StaticClass_Skipped()
    {
        var source = """
            public static class StaticHelper
            {
                static StaticHelper() { }
            }
            """;

        var results = ExtractFrom(source, threshold: 0);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Extract_AbstractClass_Skipped()
    {
        var source = """
            public abstract class AbstractBase
            {
                protected AbstractBase(int a, int b, int c, int d, int e, int f, int g, int h, int i) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Extract_TestClass_Skipped()
    {
        var source = """
            public class MyServiceTests
            {
                public MyServiceTests(int a, int b, int c, int d, int e, int f, int g, int h, int i) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Extract_CustomThreshold()
    {
        var source = """
            public class SmallService
            {
                public SmallService(int a, int b, int c) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 2);

        results.Should().ContainSingle();
        results[0].ParameterCount.Should().Be(3);
    }

    [Fact]
    public void Extract_ConstructorSignature_ContainsAllParams()
    {
        var source = """
            public class SigService
            {
                public SigService(IFoo foo, IBar bar, int count) { }
            }
            public interface IFoo { }
            public interface IBar { }
            """;

        var results = ExtractFrom(source, threshold: 2);

        results.Should().ContainSingle();
        results[0].ConstructorSignature.Should().Contain("IFoo");
        results[0].ConstructorSignature.Should().Contain("IBar");
        results[0].ConstructorSignature.Should().Contain("count");
        results[0].ConstructorSignature.Should().StartWith("SigService(");
    }

    [Fact]
    public void Extract_NoConstructors_EmptyResult()
    {
        var source = """
            public class NoCtor
            {
            }
            """;

        var results = ExtractFrom(source, threshold: 0);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MultipleFatClasses_AllFound()
    {
        var source = """
            public class FatServiceA
            {
                public FatServiceA(int a, int b, int c, int d, int e, int f, int g, int h, int i) { }
            }
            public class FatServiceB
            {
                public FatServiceB(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k) { }
            }
            public class LeanService
            {
                public LeanService(int a) { }
            }
            """;

        var results = ExtractFrom(source, threshold: 8);

        results.Should().HaveCount(2);
        results.Should().Contain(c => c.ClassName == "FatServiceA" && c.ParameterCount == 9);
        results.Should().Contain(c => c.ClassName == "FatServiceB" && c.ParameterCount == 11);
    }
}
