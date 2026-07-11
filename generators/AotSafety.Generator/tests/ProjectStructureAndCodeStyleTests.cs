namespace AotSafety.Tests;

public class ProjectStructureAndCodeStyleAnalyzerTests
{
    [Fact]
    public async Task PublicMethod_WithoutXmlDoc_ReportsJCC10004()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    public void {|#0:MethodWithoutDoc|}() { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10004", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("TestClass.MethodWithoutDoc()"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethod_WithXmlDoc_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    /// <summary>Has doc</summary>
                    public void MethodWithDoc() { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PrivateMethod_WithoutXmlDoc_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    private void PrivateMethod() { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OverrideMethod_WithoutXmlDoc_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;

                class BaseClass
                {
                    public virtual void DoWork() { }
                }

                class DerivedClass : BaseClass
                {
                    public override void DoWork() { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10004", DiagnosticSeverity.Warning).WithSpan(5, 25, 5, 31).WithArguments("BaseClass.DoWork()"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicProperty_WithoutXmlDoc_ReportsJCC10004()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    public int {|#0:Value|} { get; set; }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10004", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("TestClass.Value"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SwitchExpressionOnString_ReportsJCC10005()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    int GetKind(string kind) => {|#0:kind|} switch
                    {
                        "alpha" => 1,
                        "beta" => 2,
                        "gamma" => 3,
                        _ => 0
                    };
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10005", DiagnosticSeverity.Info).WithLocation(0).WithArguments("kind"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SwitchExpressionOnString_LessThan3Arms_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    int GetKind(string kind) => kind switch
                    {
                        "alpha" => 1,
                        _ => 0
                    };
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SwitchExpressionOnInt_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    string GetName(int id) => id switch
                    {
                        1 => "one",
                        2 => "two",
                        3 => "three",
                        _ => "unknown"
                    };
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Constructor_WithoutXmlDoc_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    public TestClass() { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsNew_ExpressionBody_ReportsJCC10008()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo { }
                class TestClass
                {
                    Foo {|#0:Bar|} => new Foo();
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10008", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("Bar"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsNew_GetterBody_ReportsJCC10008()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo { }
                class TestClass
                {
                    Foo {|#0:Bar|} { get { return new Foo(); } }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10008", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("Bar"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsNew_GetterExpressionBody_ReportsJCC10008()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo { }
                class TestClass
                {
                    Foo {|#0:Bar|} { get => new Foo(); }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC10008", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("Bar"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsCachedField_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo { }
                class TestClass
                {
                    private Foo? _bar;
                    Foo Bar => _bar ??= new Foo();
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsArrayEmpty_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    int[] Items => System.Array.Empty<int>();
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyReturnsField_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo { }
                class TestClass
                {
                    private readonly Foo _bar = new Foo();
                    Foo Bar => _bar;
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
