namespace AotSafety.Tests;

public class LocalDisposableLeakRuleTests {
    [Fact]
    public async Task LocalVar_CreateAndReturnMember_NotDisposed_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Parser {
                    public Tree Parse(string code) => new Tree();
                }
                class Tree : IDisposable {
                    public Node RootNode => new Node();
                    public void Dispose() { }
                }
                class Node { }
                class TestClass {
                    Parser _parser = new Parser();
                    public Node Method(string code) {
                        var {|#0:tree|} = _parser.Parse(code);
                        return tree?.RootNode;
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("tree"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LocalVar_New_NoDispose_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        var {|#0:x|} = new Disposable();
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("x"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingVar_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        using var x = new Disposable();
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitUsingVar_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass {
                    async Task MethodAsync() {
                        await using var x = new AsyncDisposable();
                        await x.DoWorkAsync();
                    }
                }
                class AsyncDisposable : IAsyncDisposable {
                    public Task DoWorkAsync() => Task.CompletedTask;
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExplicitDispose_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        var x = new Disposable();
                        x.DoWork();
                        x.Dispose();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitDisposeAsync_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass {
                    async Task MethodAsync() {
                        var x = new AsyncDisposable();
                        await x.DoWorkAsync();
                        await x.DisposeAsync();
                    }
                }
                class AsyncDisposable : IAsyncDisposable {
                    public Task DoWorkAsync() => Task.CompletedTask;
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ReturnVar_TransfersOwnership_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    Disposable Method() {
                        var x = new Disposable();
                        x.DoWork();
                        return x;
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AssignToField_TransfersOwnership_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    Disposable _field;
                    void Method() {
                        var x = new Disposable();
                        _field = x;
                    }
                }
                class Disposable : IDisposable {
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PassAsArgument_TransfersOwnership_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        var x = new Disposable();
                        TakeOwnership(x);
                    }
                    void TakeOwnership(Disposable d) => d.Dispose();
                }
                class Disposable : IDisposable {
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task BorrowFromField_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    Disposable _field = new Disposable();
                    void Method() {
                        var x = _field;
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeSafe_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        var x = new Disposable();
                        x.DoWork();
                        x.DisposeSafe(null);
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                static class DisposableExtensions {
                    public static void DisposeSafe(this IDisposable d, object logger) => d?.Dispose();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ReturnCoalesceThrow_TransfersOwnership_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Parser {
                    public Tree Parse(string code) => new Tree();
                }
                class Tree : IDisposable {
                    public void Dispose() { }
                }
                class TestClass {
                    Parser _parser = new Parser();
                    public Tree Method(string code) {
                        var tree = _parser.Parse(code);
                        return tree ?? throw new InvalidOperationException();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ContainerGetOrAdd_Borrow_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Concurrent;
                class TestClass {
                    ConcurrentDictionary<string, Disposable> _dict = new();
                    void Method(string key) {
                        var x = _dict.GetOrAdd(key, _ => new Disposable());
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ContainerIndexer_Borrow_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class TestClass {
                    Dictionary<string, Disposable> _dict = new();
                    void Method(string key) {
                        _dict[key] = new Disposable();
                        var x = _dict[key];
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LinqFirst_Borrow_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Linq;
                class TestClass {
                    List<Disposable> _list = new();
                    void Method() {
                        var x = _list.FirstOrDefault();
                        x?.DoWork();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureAwaitTransferToAwaitUsing_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass {
                    async Task Method() {
                        var fs = new AsyncDisposable();
                        await using var cfg = fs.ConfigureAwait(false);
                    }
                }
                class AsyncDisposable : IAsyncDisposable {
                    public ConfiguredAwaitable ConfigureAwait(bool continueOnCapturedContext) => default;
                    public ValueTask DisposeAsync() => default;
                }
                struct ConfiguredAwaitable : IAsyncDisposable {
                    public ValueTask DisposeAsync() => default;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureAwaitNotToAwaitUsing_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass {
                    async Task Method() {
                        var {|#0:fs|} = new AsyncDisposable();
                        var cfg = fs.ConfigureAwait(false);
                        cfg.DoWork();
                    }
                }
                class AsyncDisposable : IAsyncDisposable {
                    public ConfiguredAwaitable ConfigureAwait(bool continueOnCapturedContext) => default;
                    public ValueTask DisposeAsync() => default;
                }
                struct ConfiguredAwaitable {
                    public void DoWork() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("fs"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConditionalAccessDispose_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass {
                    void Method() {
                        var x = new Disposable();
                        x?.Dispose();
                    }
                }
                class Disposable : IDisposable {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task VolatileReadField_Borrow_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass {
                    CancellationTokenSource? _cts;
                    void Method() {
                        var cts = Volatile.Read(ref _cts);
                        if (cts is not null) {
                            cts.Token.WaitHandle.WaitOne(100);
                        }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task WithExpressionTransfer_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                record State(CancellationTokenSource? Cts = null);
                class TestClass {
                    State _state = new();
                    void Method() {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                        _state = _state with { Cts = cts };
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ObjectInitializerTransfer_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class Monitor {
                    public Timer? Timer { get; set; }
                }
                class TestClass {
                    Monitor? _monitor;
                    void Method() {
                        var timer = new Timer(_ => { }, null, 100, 100);
                        _monitor = new Monitor { Timer = timer };
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
