namespace AotSafety.Tests;

public class DisposableConsistencyRulesTests {
    [Fact]
    public async Task TryFinally_Dispose_ReportsJCC9105() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    void Method()
                    {
                        var x = new Disposable();
                        {|#0:try|}
                        {
                            x.DoWork();
                        }
                        finally
                        {
                            x.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9105", DiagnosticSeverity.Error).WithLocation(0).WithArguments("Dispose"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_DisposeAsync_ReportsJCC9105() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method()
                    {
                        var x = new AsyncDisposable();
                        {|#0:try|}
                        {
                            await x.DoWorkAsync();
                        }
                        finally
                        {
                            await x.DisposeAsync();
                        }
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public Task DoWorkAsync() => Task.CompletedTask;
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9105", DiagnosticSeverity.Error).WithLocation(0).WithArguments("DisposeAsync"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingVar_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    void Method()
                    {
                        using var x = new Disposable();
                        x.DoWork();
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_WithReturn_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    int Method()
                    {
                        var x = new Disposable();
                        try
                        {
                            x.DoWork();
                            return x.Value;
                        }
                        finally
                        {
                            x.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public int Value => 0;
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_ForeachRelease_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Linq;
                class TestClass
                {
                    void Method(Disposable[] clones)
                    {
                        try
                        {
                            clones.Length.ToString();
                        }
                        finally
                        {
                            foreach (var clone in clones) clone.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_FieldReceiver_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    private Disposable? _field;
                    void Method()
                    {
                        var x = _field;
                        try
                        {
                            x?.DoWork();
                        }
                        finally
                        {
                            x?.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_NonDisposeCallInFinally_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    void Method()
                    {
                        var x = new Disposable();
                        try
                        {
                            x.DoWork();
                        }
                        finally
                        {
                            x.Close();
                            x.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public void Close() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFinally_VariableDeclaredOutsideTry_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    private Disposable? _field;
                    void Method(bool flag)
                    {
                        Disposable? x = null;
                        try
                        {
                            if (flag)
                                x = new Disposable();
                            x?.DoWork();
                        }
                        finally
                        {
                            x?.Dispose();
                        }
                    }
                }
                class Disposable : IDisposable
                {
                    public void DoWork() { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeMethod_TryCatchObjectDisposedException_ReportsJCC9106() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable
                {
                    private bool _disposed;
                    private IDisposable? _inner;
                    public void Dispose()
                    {
                        if (_disposed) return; _disposed = true;
                        {|#0:try|}
                        {
                            _inner.Dispose();
                        }
                        catch (ObjectDisposedException) { }
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9106", DiagnosticSeverity.Error).WithLocation(0).WithArguments(1, "ObjectDisposedException"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsyncMethod_TryCatchException_ReportsJCC9106() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private bool _disposed;
                    private IAsyncDisposable? _inner;
                    public async ValueTask DisposeAsync()
                    {
                        if (_disposed) return; _disposed = true;
                        {|#0:try|}
                        {
                            {|#1:await|} _inner.DisposeAsync();
                        }
                        catch (Exception) { }
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9106", DiagnosticSeverity.Error).WithLocation(0).WithArguments(1, "Exception"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeMethod_NoTryCatch_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable
                {
                    private bool _disposed;
                    private IDisposable? _inner;
                    public void Dispose()
                    {
                        if (_disposed) return; _disposed = true;
                        _inner?.Dispose();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonDisposeMethod_TryCatch_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    private IDisposable? _inner;
                    public void DoWork()
                    {
                        try
                        {
                            _inner.Dispose();
                        }
                        catch (ObjectDisposedException) { }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncDisposeOnIAsyncDisposable_ReportsJCC9107() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass
                {
                    void Method(AsyncDisposable x)
                    {
                        {|#0:x.Dispose()|};
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9107", DiagnosticSeverity.Error).WithLocation(0).WithArguments("AsyncDisposable"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AsyncDisposeOnIAsyncDisposable_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method(AsyncDisposable x)
                    {
                        await x.DisposeAsync();
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncDisposeOnIDisposable_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass
                {
                    void Method(SyncDisposable x)
                    {
                        x.Dispose();
                    }
                }
                class SyncDisposable : IDisposable
                {
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncDisposeOnDualInterface_ReportsJCC9107() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass
                {
                    void Method(DualDisposable x)
                    {
                        {|#1:x.Dispose()|};
                    }
                }
                class {|#0:DualDisposable|} : IDisposable, IAsyncDisposable
                {
                    public void Dispose() { }
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9103", DiagnosticSeverity.Error).WithLocation(0).WithArguments("DualDisposable"),
                new DiagnosticResult("JCC9107", DiagnosticSeverity.Error).WithLocation(1).WithArguments("DualDisposable"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncDispose_OnCancellationTokenSource_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass
                {
                    void Method(CancellationTokenSource cts)
                    {
                        cts.Dispose();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncDisposeOnIAsyncDisposable_InsideDisposeMethod_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IDisposable
                {
                    private bool _disposed;
                    private AsyncDisposable? _inner;
                    public void Dispose()
                    {
                        if (_disposed) return; _disposed = true;
                        _inner.Dispose();
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FireAndForget_Discard_InDisposeAsync_ReportsJCC9200() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private bool _disposed;
                    private AsyncDisposable? _inner;
                    public async ValueTask DisposeAsync()
                    {
                        if (_disposed) return; _disposed = true;
                        {|#0:_ = _inner.DisposeAsync()|};
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9200", DiagnosticSeverity.Error).WithLocation(0).WithArguments("DisposeAsync", 10),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FireAndForget_BareCall_InDisposeAsync_ReportsJCC9200() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private bool _disposed;
                    private AsyncDisposable? _inner;
                    public async ValueTask DisposeAsync()
                    {
                        if (_disposed) return; _disposed = true;
                        {|#0:_inner.DisposeAsync()|};
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9200", DiagnosticSeverity.Error).WithLocation(0).WithArguments("DisposeAsync", 10),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Await_InDisposeAsync_NoJCC9200() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private bool _disposed;
                    private AsyncDisposable? _inner;
                    public async ValueTask DisposeAsync()
                    {
                        if (_disposed) return; _disposed = true;
                        await _inner.DisposeAsync().ConfigureAwait(false);
                    }
                }
                class AsyncDisposable : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Dispose_NoGuard_ReportsJCC9201() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable
                {
                    private int _disposed;
                    public void {|#0:Dispose|}()
                    {
                        _disposed = 1;
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9201", DiagnosticSeverity.Error).WithLocation(0).WithArguments("TestClass"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Dispose_WithInterlockedGuard_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                class TestClass : IDisposable
                {
                    private int _disposed;
                    public void Dispose()
                    {
                        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Dispose_WithIfReturnGuard_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable
                {
                    private bool _disposed;
                    public void Dispose()
                    {
                        if (_disposed) return;
                        _disposed = true;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_NoGuard_ReportsJCC9202() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private int _disposed;
                    public async ValueTask {|#0:DisposeAsync|}()
                    {
                        _disposed = 1;
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9202", DiagnosticSeverity.Error).WithLocation(0).WithArguments("TestClass"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_WithInterlockedGuard_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private int _disposed;
                    public async ValueTask DisposeAsync()
                    {
                        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_WithIfReturnGuard_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class TestClass : IAsyncDisposable
                {
                    private bool _disposed;
                    public async ValueTask DisposeAsync()
                    {
                        if (_disposed) return;
                        _disposed = true;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingVar_OnDualInterfaceBclType_ReportsJCC9104() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.IO;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method()
                    {
                        {|#0:using|} var stream = new FileStream("test", FileMode.Open, FileAccess.Read);
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9104", DiagnosticSeverity.Error).WithLocation(0).WithArguments("FileStream"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingVar_OnCancellationTokenSource_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method()
                    {
                        using var cts = new CancellationTokenSource();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UsingVar_OnMemoryStream_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.IO;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method()
                    {
                        using var ms = new MemoryStream();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AwaitUsingVar_OnDualInterfaceBclType_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.IO;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task Method()
                    {
                        await using var stream = new FileStream("test", FileMode.Open, FileAccess.Read);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}