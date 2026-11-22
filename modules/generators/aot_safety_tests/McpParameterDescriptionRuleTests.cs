namespace AotSafety.Tests;

/// <summary>
/// JCC2010 (McpParameterDescriptionRule) 单元测试 — 验证 MCP 工具参数描述检查。
/// 覆盖: 有描述/空描述/WellKnownParam/无特性/CancellationToken跳过/McpToolOptions跳过/非McpTool方法。
/// </summary>
public class McpParameterDescriptionRuleTests {
    private const string StubAttributes = """
        using System;
        using System.Threading;

        [AttributeUsage(AttributeTargets.Method)]
        class McpToolAttribute : Attribute {
            public McpToolAttribute(string name, string desc, string cat) { }
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        class McpToolParameterAttribute : Attribute {
            public McpToolParameterAttribute(string description) { }
            public McpToolParameterAttribute(WellKnownParam wellKnown) { }
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        class McpToolOptionsAttribute : Attribute { }

        enum WellKnownParam { FilePath, PrNumber, Limit }
        """;

    [Fact]
    public async Task HasDescription_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([McpToolParameter("参数描述")] string arg) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EmptyDescription_ReportsJCC2010() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([{|#0:McpToolParameter("")|}] string arg) { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC2010", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("arg"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task WellKnownParam_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([McpToolParameter(WellKnownParam.FilePath)] string file_path) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NoMcpToolParameter_ReportsJCC2010() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method({|#0:string arg|}) { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC2010", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("arg"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CancellationToken_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([McpToolParameter("参数描述")] string arg, CancellationToken ct) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task McpToolOptions_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([McpToolParameter("参数描述")] string arg, [McpToolOptions] object options) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonMcpToolMethod_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    void Method(string arg) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task WhitespaceOnlyDescription_ReportsJCC2010() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([{|#0:McpToolParameter("   ")|}] string arg) { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC2010", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("arg"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CallbackType_NoReport() {
        var test = new CSharpAnalyzerTest<McpParameterRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = StubAttributes + """
                class ToolProgressCallback { }
                class TestClass
                {
                    [McpTool("test_tool", "test", "test")]
                    void Method([McpToolParameter("参数描述")] string arg, ToolProgressCallback? onProgress) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
