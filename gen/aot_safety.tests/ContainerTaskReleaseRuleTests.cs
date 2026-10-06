namespace AotSafety.Tests;

public class ContainerTaskReleaseRuleTests {
    [Fact]
    public async Task TaskArrayField_DisposeEmpty_ReportsJCC9307() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IDisposable {
                    private readonly Task[] {|#0:_tasks|} = Array.Empty<Task>();
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9307", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_tasks"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskArrayField_DisposeAsyncWhenAll_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly Task[] _tasks = Array.Empty<Task>();
                    public async ValueTask DisposeAsync() {
                        await Task.WhenAll(_tasks).ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListTaskField_DisposeEmpty_ReportsJCC9307() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IDisposable {
                    private readonly List<Task> {|#0:_tasks|} = new();
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9307", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_tasks"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListTaskField_DisposeAsyncWhenAll_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<Task> _tasks = new();
                    public async ValueTask DisposeAsync() {
                        await Task.WhenAll(_tasks).ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonTaskCollection_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass : IDisposable {
                    private readonly List<IDisposable> _items = new();
                    public void Dispose() {
                        foreach (var x in _items) x.Dispose();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskArrayField_NoDisposeMethod_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass {
                    private readonly Task[] _tasks = Array.Empty<Task>();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task HelperMethodWhenAll_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<Task> _tasks = new();
                    public async ValueTask DisposeAsync() {
                        await WaitForAllAsync().ConfigureAwait(false);
                    }
                    private ValueTask WaitForAllAsync() {
                        return new ValueTask(Task.WhenAll(_tasks));
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldBackedLocalWhenAll_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<Task> _tasks = new();
                    public async ValueTask DisposeAsync() {
                        var snapshot = _tasks;
                        await Task.WhenAll(snapshot).ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DictionaryTaskField_DisposeAsyncWhenAllValues_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly Dictionary<string, Task> _tasks = new();
                    public async ValueTask DisposeAsync() {
                        await Task.WhenAll(_tasks.Values).ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
