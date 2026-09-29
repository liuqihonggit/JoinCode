namespace Core.Tests.Context.Compression;

public class CodeContentCompressorTests {
    private readonly CodeContentCompressor _compressor = new();
    private readonly ITestOutputHelper _output;

    public CodeContentCompressorTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    public void Name_ShouldReturnCorrectValue() {
        _compressor.Name.Should().Be("CodeContentCompressor");
    }

    [Fact]
    public void SupportedContentTypes_ShouldContainCode() {
        _compressor.SupportedContentTypes.Should().Contain(ContentType.Code);
    }

    [Fact]
    public void CanHandle_CodeContent_ShouldReturnTrue() {
        var code = "public class Test { public void Method() { var x = 1; Console.WriteLine(x); } }";
        _compressor.CanHandle(code, ContentType.Code).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonCodeContent_ShouldReturnFalse() {
        var content = "Some dialogue content";
        _compressor.CanHandle(content, ContentType.Dialogue).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyContent_ShouldReturnFalse() {
        _compressor.CanHandle("", ContentType.Code).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ShortContent_ShouldReturnFalse() {
        _compressor.CanHandle("abc", ContentType.Code).Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_EmptyContent_ShouldReturnEmpty() {
        var result = await _compressor.CompressAsync("", CompressionOptions.Default).ConfigureAwait(true);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveClassSignature() {
        var code = @"
using System;

public class TestClass
{
    public void Method() {
        Console.WriteLine(""Hello"");
    }
}";

        var result = await _compressor.CompressAsync(code, CompressionOptions.ForCode).ConfigureAwait(true);

        result.Should().Contain("public class TestClass");
        result.Should().Contain("public void Method()");
    }

    [Fact]
    public async Task CompressAsync_ShouldRemoveMethodBody() {
        var code = @"
public class TestClass
{
    public void Method() {
        var x = 1;
        var y = 2;
        Console.WriteLine(x + y);
    }
}";

        // 使用 Aggressive 选项来确保方法体被移除
        var options = new CompressionOptions {
            PreserveSignatures = true,
            PreserveTypeDefinitions = true,
            MaxMethodBodyLines = 0,
            PreserveImports = false,
            PreserveComments = false,
            PreserveDocumentation = false,
            PreserveConstants = false,
            PreserveEnums = false
        };

        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        // 输出实际结果用于调试
        _output.WriteLine($"Compressed result:\n{result}");

        result.Should().NotContain("var x = 1");
        result.Should().NotContain("var y = 2");
        result.Should().Contain("method body omitted");
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveImports() {
        var code = @"
using System;
using System.Collections.Generic;

public class Test { }";

        var options = new CompressionOptions { PreserveImports = true };
        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        result.Should().Contain("using System");
        result.Should().Contain("using System.Collections.Generic");
    }

    [Fact]
    public async Task CompressAsync_ShouldRemoveImportsWhenNotPreserved() {
        var code = @"
using System;
using System.Collections.Generic;
using System.Linq;

public class Test {
    public void Method() {
        var x = 1;
        Console.WriteLine(x);
    }
}";

        var options = new CompressionOptions { PreserveImports = false };
        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        result.Should().NotContain("using System");
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveKeyComments() {
        var code = @"
public class Test
{
    // TODO: Fix this method
    public void Method() { }
}";

        var result = await _compressor.CompressAsync(code, CompressionOptions.ForCode).ConfigureAwait(true);

        result.Should().Contain("TODO");
    }

    [Fact]
    public async Task CompressAsync_ShouldRemoveRegularComments() {
        var code = @"
public class Test
{
    // This is a regular comment
    public void Method() { }
}";

        var options = new CompressionOptions { PreserveComments = true };
        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        result.Should().NotContain("This is a regular comment");
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveInterfaceDefinition() {
        var code = @"
public interface ITest
{
    void Method();
}";

        var result = await _compressor.CompressAsync(code, CompressionOptions.ForCode).ConfigureAwait(true);

        result.Should().Contain("public interface ITest");
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveEnumDefinition() {
        var code = @"
public enum Status
{
    Active,
    Inactive
}";

        var options = new CompressionOptions { PreserveEnums = true };
        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        result.Should().Contain("public enum Status");
    }

    [Fact]
    public async Task CompressAsync_ShouldPreserveConstants() {
        var code = @"
public class Test
{
    public const int MAX_VALUE = 100;
    public readonly string Name = ""Test"";
}";

        var options = new CompressionOptions { PreserveConstants = true };
        var result = await _compressor.CompressAsync(code, options).ConfigureAwait(true);

        result.Should().Contain("MAX_VALUE");
    }

    [Fact]
    public void EstimateCompressionRatio_CodeWithLargeMethodBodies_ShouldReturnLowerRatio() {
        var code = @"
public class Test
{
    public void Method1() {
        var a = 1;
        var b = 2;
        var c = 3;
        var d = 4;
        var e = 5;
    }

    public void Method2() {
        var x = 1;
        var y = 2;
        var z = 3;
    }
}";

        var ratio = _compressor.EstimateCompressionRatio(code, CompressionOptions.ForCode);

        ratio.Should().BeLessThan(1.0);
        ratio.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateCompressionRatio_EmptyContent_ShouldReturnOne() {
        var ratio = _compressor.EstimateCompressionRatio("", CompressionOptions.Default);
        ratio.Should().Be(1.0);
    }

    [Fact]
    public void EstimateCompressionRatio_NoMethodBodies_ShouldReturnHigherRatio() {
        var code = @"
public interface ITest
{
    void Method1();
    void Method2();
}";

        var ratio = _compressor.EstimateCompressionRatio(code, CompressionOptions.ForCode);

        ratio.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task CompressAsync_CancellationRequested_ShouldThrowOperationCanceledException() {
        var code = "public class Test { public void Method() { } }";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _compressor.CompressAsync(code, CompressionOptions.Default, cts.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CompressAsync_ShouldHandleExpressionBodiedMembers() {
        var code = @"
public class Test
{
    public int Value => 42;
    public string Name => GetName();
}";

        var result = await _compressor.CompressAsync(code, CompressionOptions.ForCode).ConfigureAwait(true);

        result.Should().Contain("public int Value => 42");
    }

    // ===== TASK031 阶段2.1 提取的 internal 子方法确定性测试 =====

    [Fact]
    public void ClassifyCodeLine_EmptyLine_ShouldReturnSkip() {
        var kind = CodeContentCompressor.ClassifyCodeLine("", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Skip);
    }

    [Fact]
    public void ClassifyCodeLine_WhitespaceLine_ShouldReturnSkip() {
        var kind = CodeContentCompressor.ClassifyCodeLine("   ", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Skip);
    }

    [Fact]
    public void ClassifyCodeLine_RegionDirective_ShouldReturnSkip() {
        var kind = CodeContentCompressor.ClassifyCodeLine("#region MyRegion", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Skip);
    }

    [Fact]
    public void ClassifyCodeLine_EndRegionDirective_ShouldReturnSkip() {
        var kind = CodeContentCompressor.ClassifyCodeLine("#endregion", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Skip);
    }

    [Fact]
    public void ClassifyCodeLine_UsingStatement_ShouldReturnImport() {
        var kind = CodeContentCompressor.ClassifyCodeLine("using System;", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Import);
    }

    [Fact]
    public void ClassifyCodeLine_ImportStatement_ShouldReturnImport() {
        var kind = CodeContentCompressor.ClassifyCodeLine("import foo from 'bar'", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.Import);
    }

    [Fact]
    public void ClassifyCodeLine_DocComment_WhenPreserved_ShouldReturnDocComment() {
        var options = new CompressionOptions { PreserveDocumentation = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("/// <summary>", options, false);
        kind.Should().Be(CodeLineKind.DocComment);
    }

    [Fact]
    public void ClassifyCodeLine_DocComment_WhenNotPreserved_ShouldFallThrough() {
        var options = new CompressionOptions { PreserveDocumentation = false, PreserveComments = false };
        // 不保留文档注释时应继续往下分类（最终作为 NormalLine）
        var kind = CodeContentCompressor.ClassifyCodeLine("/// <summary>", options, false);
        kind.Should().Be(CodeLineKind.NormalLine);
    }

    [Fact]
    public void ClassifyCodeLine_Comment_WhenPreserved_ShouldReturnComment() {
        var options = new CompressionOptions { PreserveComments = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("// regular comment", options, false);
        kind.Should().Be(CodeLineKind.Comment);
    }

    [Fact]
    public void ClassifyCodeLine_TypeDefinition_WhenPreserved_ShouldReturnTypeDefinition() {
        var options = new CompressionOptions { PreserveTypeDefinitions = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("public class Foo", options, false);
        kind.Should().Be(CodeLineKind.TypeDefinition);
    }

    [Fact]
    public void ClassifyCodeLine_InterfaceDefinition_WhenPreserved_ShouldReturnTypeDefinition() {
        var options = new CompressionOptions { PreserveTypeDefinitions = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("public interface IFoo", options, false);
        kind.Should().Be(CodeLineKind.TypeDefinition);
    }

    [Fact]
    public void ClassifyCodeLine_EnumDefinition_WhenPreserved_ShouldReturnEnumDefinition() {
        var options = new CompressionOptions { PreserveEnums = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("public enum Status", options, false);
        kind.Should().Be(CodeLineKind.EnumDefinition);
    }

    [Fact]
    public void ClassifyCodeLine_ConstantDefinition_WhenPreserved_ShouldReturnConstantDefinition() {
        var options = new CompressionOptions { PreserveConstants = true };
        var kind = CodeContentCompressor.ClassifyCodeLine("public const int MAX = 100;", options, false);
        kind.Should().Be(CodeLineKind.ConstantDefinition);
    }

    [Fact]
    public void ClassifyCodeLine_MethodSignature_ShouldReturnMethodSignature() {
        // PreserveConstants=false 以避免被 ConstantDefinition 正则提前匹配（原代码分类顺序）
        var options = new CompressionOptions { PreserveConstants = false };
        var kind = CodeContentCompressor.ClassifyCodeLine("public void Method() {", options, false);
        kind.Should().Be(CodeLineKind.MethodSignature);
    }

    [Fact]
    public void ClassifyCodeLine_NormalLine_WhenNotInMethodBody_ShouldReturnNormalLine() {
        var kind = CodeContentCompressor.ClassifyCodeLine("var x = 1;", CompressionOptions.Default, false);
        kind.Should().Be(CodeLineKind.NormalLine);
    }

    [Fact]
    public void ClassifyCodeLine_MethodBodyLine_WhenInMethodBody_ShouldReturnMethodBodyLine() {
        var kind = CodeContentCompressor.ClassifyCodeLine("var x = 1;", CompressionOptions.Default, true);
        kind.Should().Be(CodeLineKind.MethodBodyLine);
    }

    [Fact]
    public void ClassifyCodeLine_TypeDefinition_WhenNotPreserved_ShouldFallThroughToMethodSignature() {
        // 不保留类型定义和常量时，"public class Foo {" 匹配方法签名正则 → MethodSignature
        var options = new CompressionOptions { PreserveTypeDefinitions = false, PreserveConstants = false };
        var kind = CodeContentCompressor.ClassifyCodeLine("public class Foo {", options, false);
        kind.Should().Be(CodeLineKind.MethodSignature);
    }

    [Fact]
    public void BuildMethodBodyContent_MaxZero_ShouldReturnOmissionPlaceholder() {
        var lines = new[] { "void M() {", "    var x = 1;", "}" };
        var result = CodeContentCompressor.BuildMethodBodyContent(0, 2, 0, lines);
        result.Should().Be("    // ... method body omitted ..." + Environment.NewLine);
    }

    [Fact]
    public void BuildMethodBodyContent_WithinLimit_ShouldReturnOriginalBody() {
        var lines = new[] { "void M() {", "    var x = 1;", "    var y = 2;", "}" };
        // 方法体从行1到行3（2行），maxMethodBodyLines=5 → 保留
        var result = CodeContentCompressor.BuildMethodBodyContent(0, 3, 5, lines);
        result.Should().Contain("var x = 1");
        result.Should().Contain("var y = 2");
        result.Should().NotContain("method body omitted");
    }

    [Fact]
    public void BuildMethodBodyContent_ExceedsLimit_ShouldReturnOmissionPlaceholder() {
        var lines = new[] { "void M() {", "    var x = 1;", "    var y = 2;", "}" };
        // 方法体从行1到行3（3行差），maxMethodBodyLines=1 → 1 < 3 省略
        var result = CodeContentCompressor.BuildMethodBodyContent(0, 3, 1, lines);
        result.Should().Be("    // ... method body omitted ..." + Environment.NewLine);
    }

    [Fact]
    public void BuildMethodBodyContent_ExactLimit_ShouldReturnOriginalBody() {
        var lines = new[] { "void M() {", "    var x = 1;", "}" };
        // 方法体从行1到行2（差2），maxMethodBodyLines=2 → 2 <= 2 保留
        var result = CodeContentCompressor.BuildMethodBodyContent(0, 2, 2, lines);
        result.Should().Contain("var x = 1");
        result.Should().NotContain("method body omitted");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("no braces", 0)]
    [InlineData("{", 1)]
    [InlineData("{{", 2)]
    [InlineData("}", -1)]
    [InlineData("}}", -2)]
    [InlineData("{}", 0)]
    [InlineData("{ { } }", 0)]
    [InlineData("} {", 0)]
    public void CountBraces_VariousInputs_ShouldReturnCorrectCount(string line, int expected) {
        CodeContentCompressor.CountBraces(line).Should().Be(expected);
    }
}