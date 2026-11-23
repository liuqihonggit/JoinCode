namespace AotSafety.Tests;

/// <summary>
/// JCC1009 (NestedIfRule) 单元测试 — 验证 if 嵌套深度计算。
/// 覆盖: 无嵌套/2层/3层/else-if链/else-if链内嵌套/多级else-if/switch/foreach/生成代码。
/// </summary>
public class NestedIfRuleTests {
    [Fact]
    public async Task NoNesting_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b)
                    {
                        if (a) { }
                        if (b) { }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TwoLevelNesting_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b)
                    {
                        if (a)
                        {
                            if (b) { }
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeLevelNesting_ReportsJCC1009() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b, bool c)
                    {
                        {|#0:if|} (a)
                        {
                            if (b)
                            {
                                if (c) { }
                            }
                        }
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC1009", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("3", "2"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ElseIfChain_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b, bool c, bool d)
                    {
                        if (a) { }
                        else if (b) { }
                        else if (c) { }
                        else if (d) { }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 可疑案例 1 的简化: else-if 链内部 foreach 里嵌套 if, 应该是 2 层不报告.
    /// bug 版本会误报 3 层 (else-if 链的 ElseClause 边界未正确识别).
    /// </summary>
    [Fact]
    public async Task ElseIfChain_WithNestedIfInForEach_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Method(bool a, bool b, bool c, List<int> items)
                    {
                        if (a)
                        {
                            if (b) { }
                        }
                        else if (c)
                        {
                            foreach (var x in items)
                            {
                                if (x > 0) { }
                            }
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 多级 else-if 链 (if-a else if-b else if-c) 内部嵌套 if, 应该是 2 层不报告.
    /// </summary>
    [Fact]
    public async Task MultiLevelElseIfChain_WithNestedIf_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b, bool c, bool d)
                    {
                        if (a) { }
                        else if (b) { }
                        else if (c)
                        {
                            if (d) { }
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// else-if 链内部 3 层 if 嵌套应该报告 (else-if 不计入深度, 但 if 之间嵌套正常计算).
    /// </summary>
    [Fact]
    public async Task ElseIfChain_WithThreeLevelNesting_ReportsJCC1009() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(bool a, bool b, bool c, bool d, bool e)
                    {
                        if (a) { }
                        else {|#0:if|} (b)
                        {
                            if (c)
                            {
                                if (d)
                                {
                                    if (e) { }
                                }
                            }
                        }
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC1009", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("4", "2"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SwitchStatement_WithIf_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method(int x, bool a, bool b)
                    {
                        switch (x)
                        {
                            case 1:
                                if (a)
                                {
                                    if (b) { }
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ForEachLoop_WithIf_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Method(List<int> items, bool a)
                    {
                        foreach (var x in items)
                        {
                            if (a) { }
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ForEachLoop_WithNestedIf_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Method(List<int> items, bool a, bool b)
                    {
                        foreach (var x in items)
                        {
                            if (a)
                            {
                                if (b) { }
                            }
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ForEachLoop_WithThreeLevelNesting_ReportsJCC1009() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Method(List<int> items, bool a, bool b, bool c)
                    {
                        foreach (var x in items)
                        {
                            {|#0:if|} (a)
                            {
                                if (b)
                                {
                                    if (c) { }
                                }
                            }
                        }
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC1009", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("3", "2"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 表达式体方法没有 Body, 应直接跳过不分析.
    /// </summary>
    [Fact]
    public async Task ExpressionBodyMethod_NoReport() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method() => DoSomething();
                    void DoSomething() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task GeneratedCode_Skipped() {
        var test = new CSharpAnalyzerTest<GuardClauseRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState = {
                Sources = {
                    ("GeneratedFile.g.cs", """
                        // <auto-generated/>
                        class TestClass
                        {
                            void Method(bool a, bool b, bool c)
                            {
                                if (a)
                                {
                                    if (b)
                                    {
                                        if (c) { }
                                    }
                                }
                            }
                        }
                        """),
                },
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
