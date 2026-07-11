namespace AotSafety.Tests;

public class DynamicAndAsyncSafetyAnalyzerTests
{
    [Fact]
    public async Task DynamicKeyword_InVariableDeclaration_ReportsJCC1004()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method()
                    {
                        {|#0:dynamic|} x = 42;
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1004", DiagnosticSeverity.Error).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DynamicKeyword_InParameter_ReportsJCC1004()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method({|#0:dynamic|} arg)
                    {
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1004", DiagnosticSeverity.Error).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task VarKeyword_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void Method()
                    {
                        var x = 42;
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncVoid_Method_ReportsJCC3005()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    // Main 入口调用 static BadMethod,避免 CS0120 和 JCC7001 双重问题
                    static void Main() => BadMethod();
                    static async {|#0:void|} BadMethod()
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC3005", DiagnosticSeverity.Error).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncTask_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task GoodMethod()
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncVoid_UiEventHandler_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    async void Button_Click(object sender, System.EventArgs e)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskResult_ReportsJCC3006()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task MethodAsync()
                    {
                        var task = SomeAsync();
                        var result = task.{|#0:Result|};
                    }

                    Task<int> SomeAsync() => Task.FromResult(42);
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC3006", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(".Result"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskWait_ReportsJCC3006()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task MethodAsync()
                    {
                        var task = SomeAsync();
                        task.{|#0:Wait|}();
                    }

                    Task SomeAsync() => Task.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC3006", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(".Wait()"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonTaskResult_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    struct MyResult
                    {
                        public int Result => 42;
                    }
                    void Method()
                    {
                        var r = new MyResult();
                        var x = r.Result;
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TooManyParameters_ReportsJCC1006()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void {|#0:ManyParams|}(int a, int b, int c, int d, int e, int f, int g, int h, int i) { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1006", DiagnosticSeverity.Info).WithLocation(0).WithArguments("ManyParams", "9"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ThreeOrFewerParameters_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void OkParams(int a, int b, int c) { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ParallelForEach_ReportsJCC6009()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass
                {
                    void Method(List<int> items)
                    {
                        {|#0:Parallel.ForEach(items, item => Process(item))|};
                    }

                    void Process(int x) { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC6009", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonParallelForEach_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Method(List<int> items)
                    {
                        items.ForEach(item => Process(item));
                    }

                    void Process(int x) { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingInCsFile_ReportsJCC1005()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("d:\\project\\src\\SomeFile.cs", """
                        {|#0:using System.Collections.Generic;|}
                        class TestClass
                        {
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1005", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("System.Collections.Generic"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingInGlobalUsings_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("d:\\project\\src\\GlobalUsings.cs", """
                        global using System;
                        global using System.Collections.Generic;
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SequentialAwaitInLoop_ReportsJCC3007()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task ProcessAllAsync(List<string> items)
                    {
                        foreach (var item in items)
                        {
                            {|#0:await ProcessAsync(item).ConfigureAwait(false)|};
                        }
                    }

                    Task ProcessAsync(string s) => Task.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC3007", DiagnosticSeverity.Info).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitOutsideLoop_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task MethodAsync()
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitWithResultInLoop_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task ProcessAllAsync(List<string> items)
                    {
                        foreach (var item in items)
                        {
                            var result = await ProcessAsync(item).ConfigureAwait(false);
                            UseResult(result);
                        }
                    }

                    Task<int> ProcessAsync(string s) => Task.FromResult(42);
                    void UseResult(int r) { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitWithoutConfigureAwaitInLib_ReportsJCC3008()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("d:\\jcc-w2\\lib\\Infrastructure\\SomeService.cs", """
                        {|#1:using System.Threading.Tasks;|}
                        class SomeService
                        {
                            async Task MethodAsync()
                            {
                                {|#0:await Task.Delay(100)|};
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC3008", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitWithConfigureAwaitFalse_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("d:\\jcc-w2\\lib\\Infrastructure\\SomeService.cs", """
                        {|#0:using System.Threading.Tasks;|}
                        class SomeService
                        {
                            async Task MethodAsync()
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1005", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("System.Threading.Tasks"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitWithoutConfigureAwaitInHost_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AotSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("d:\\jcc-w2\\src\\hosts\\JoinCode\\Program.cs", """
                        {|#0:using System.Threading.Tasks;|}
                        class Program
                        {
                            static async Task Main()
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1005", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("System.Threading.Tasks"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
