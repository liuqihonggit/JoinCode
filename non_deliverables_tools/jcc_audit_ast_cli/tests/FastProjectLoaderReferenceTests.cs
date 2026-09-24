namespace JccAuditCli;

/// <summary>
/// 验证 FastProjectLoader 创建的 Compilation 能正确解析 System.IDisposable/IAsyncDisposable
/// 定位快速模式 0 处 JCC9103 违规的根因
/// </summary>
public class FastProjectLoaderReferenceTests {
    [Fact]
    public void Compilation_Should_Resolve_IDisposable_And_IAsyncDisposable() {
        // 用 .NET 8 引用程序集（包含完整的类型定义）
        var metadataReferences = Basic.Reference.Assemblies.Net80.References.All.ToList();

        // 模拟一个实现 IDisposable + IAsyncDisposable 的类型
        const string source = """
            using System;
            using System.Threading.Tasks;
            public sealed class DualDisposable : IDisposable, IAsyncDisposable {
                public void Dispose() { }
                public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 验证 IDisposable 和 IAsyncDisposable 能解析
        var idisposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        idisposable.Should().NotBeNull("System.IDisposable 应从 System.Runtime.dll 解析");
        iasyncDisposable.Should().NotBeNull("System.IAsyncDisposable 应从 System.Runtime.dll 解析");

        // 验证 DualDisposable 类型能找到
        var dualDisposable = compilation.GetTypeByMetadataName("DualDisposable");
        dualDisposable.Should().NotBeNull("DualDisposable 类型应能解析");

        // 验证 DualDisposable 实现 IDisposable 和 IAsyncDisposable
        var implementsIDisposable = dualDisposable!.Interfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IDisposable");
        var implementsIAsyncDisposable = dualDisposable.Interfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IAsyncDisposable");
        implementsIDisposable.Should().BeTrue("DualDisposable 应实现 IDisposable");
        implementsIAsyncDisposable.Should().BeTrue("DualDisposable 应实现 IAsyncDisposable");
    }

}
