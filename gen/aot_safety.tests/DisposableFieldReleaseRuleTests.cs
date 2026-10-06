namespace AotSafety.Tests;

public class DisposableFieldReleaseRuleTests {
    [Fact]
    public async Task NewField_NotDisposed_ReportsJCC9301() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private readonly SemaphoreSlim {|#0:_sem|} = new(0, 1);
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9301", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_sem"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FactoryField_NotDisposed_ReportsJCC9301() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private IDisposable {|#0:_field|};
                    void Setup() { _field = Create(); }
                    static IDisposable Create() => new SemaphoreSlim(0, 1);
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9301", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_field"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InjectedField_NotDisposed_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable {
                    private readonly IDisposable _injected;
                    public TestClass(IDisposable dep) { _injected = dep; }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NewField_Disposed_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private readonly SemaphoreSlim _sem = new(0, 1);
                    public void Dispose() { _sem.Dispose(); }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FactoryField_Disposed_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private IDisposable _field;
                    void Setup() { _field = Create(); }
                    static IDisposable Create() => new SemaphoreSlim(0, 1);
                    public void Dispose() { _field?.Dispose(); }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncField_NotDisposed_ReportsJCC9301() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                using System.Threading;
                class TestClass : IAsyncDisposable {
                    private readonly CancellationTokenSource {|#0:_cts|} = new();
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9301", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_cts"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncField_DisposedInDisposeAsync_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                using System.Threading;
                class TestClass : IAsyncDisposable {
                    private readonly CancellationTokenSource _cts = new();
                    public async ValueTask DisposeAsync() { _cts.Dispose(); await ValueTask.CompletedTask; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NewFieldInConstructor_NotDisposed_ReportsJCC9301() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private readonly SemaphoreSlim {|#0:_sem|};
                    public TestClass() { _sem = new(0, 1); }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9301", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_sem"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InjectedFieldWithNewField_OnlyNewFieldReported() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private readonly IDisposable _injected;
                    private readonly SemaphoreSlim {|#0:_sem|} = new(0, 1);
                    public TestClass(IDisposable dep) { _injected = dep; }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9301", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_sem"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NullInitializerField_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable {
                    private SemaphoreSlim? _sem;
                    public void Dispose() { _sem?.Dispose(); }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
