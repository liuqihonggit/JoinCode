namespace AotSafety.Tests;

public class ContainerCollectionReleaseRuleTests {
    [Fact]
    public async Task ListDisposableField_DisposeEmpty_ReportsJCC9306() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass : IDisposable {
                    private readonly List<IDisposable> {|#0:_items|} = new();
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9306", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_items"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListDisposableField_DisposeForeachReleases_NoDiagnostic() {
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
    public async Task DictionaryDisposableField_DisposeEmpty_ReportsJCC9306() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Concurrent;
                using System.Collections.Generic;
                class TestClass : IDisposable {
                    private readonly ConcurrentDictionary<string, IDisposable> {|#0:_map|} = new();
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9306", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_map"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DictionaryDisposableField_DisposeForeachValuesReleases_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass : IDisposable {
                    private readonly Dictionary<string, IDisposable> _map = new();
                    public void Dispose() {
                        foreach (var x in _map.Values) x.Dispose();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NoContainerField_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable {
                    private readonly int _count = 0;
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposableArrayField_DisposeEmpty_ReportsJCC9306() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable {
                    private readonly IDisposable[] {|#0:_items|} = new IDisposable[0];
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9306", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_items"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListDisposableField_NoDisposeMethod_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass {
                    private readonly List<IDisposable> _items = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskWhenAllFieldSelect_DisposeAsync_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<IAsyncDisposable> _items = new();
                    public ValueTask DisposeAsync() {
                        return new ValueTask(Task.WhenAll(_items.Select(x => x.DisposeAsync().AsTask())));
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task HelperMethodLocalSnapshotFromField_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass : IDisposable {
                    private readonly List<IDisposable> _items = new();
                    public void Dispose() {
                        ReleaseAll();
                    }
                    private void ReleaseAll() {
                        List<IDisposable> snapshot;
                        snapshot = _items;
                        foreach (var x in snapshot) x.Dispose();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsyncDelegatesHelperMethod_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<IAsyncDisposable> _items = new();
                    public async ValueTask DisposeAsync() {
                        await ReleaseAllAsync().ConfigureAwait(false);
                    }
                    private async ValueTask ReleaseAllAsync() {
                        foreach (var x in _items) await x.DisposeAsync().ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListTaskField_TaskExcluded_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IDisposable {
                    private readonly List<Task> _tasks = new();
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PostStopAsyncReleasesField_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable {
                    private readonly List<IAsyncDisposable> _items = new();
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                    protected virtual async ValueTask PostStopAsync() {
                        foreach (var x in _items) await x.DisposeAsync().ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
