namespace AotSafety.Tests;

public class EventSubscriptionLeakRuleTests {
    [Fact]
    public async Task FieldEvent_MethodHandler_NotCancelled_ReportsJCC9308() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    private readonly Source _source = new();
                    public TestClass() { {|#0:_source.Changed += OnChanged|}; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_source", "Changed"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldEvent_MethodHandler_Cancelled_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    private readonly Source _source = new();
                    public TestClass() { _source.Changed += OnChanged; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { _source.Changed -= OnChanged; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldEvent_Lambda_NotCancelled_ReportsJCC9308() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    private readonly Source _source = new();
                    public TestClass() { {|#0:_source.Changed += (s, e) => { }|}; }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_source", "Changed"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SelfEvent_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class TestClass : IDisposable {
                    public event EventHandler? Changed;
                    public TestClass() { Changed += OnChanged; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NoDisposeMethod_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass {
                    private readonly Source _source = new();
                    public TestClass() { _source.Changed += OnChanged; }
                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PropertyEvent_MethodHandler_NotCancelled_ReportsJCC9308() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    public Source SourceProp { get; } = new();
                    public TestClass() { {|#0:SourceProp.Changed += OnChanged|}; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("SourceProp", "Changed"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ParameterEvent_NotCancelled_ReportsJCC9308() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    public TestClass(Source source) { {|#0:source.Changed += OnChanged|}; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("source", "Changed"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LocalVariableEvent_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source { public event EventHandler? Changed; }
                class TestClass : IDisposable {
                    public void Setup() {
                        var source = new Source();
                        source.Changed += OnChanged;
                    }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldEvent_DisposeAsync_Cancelled_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class Source { public event EventHandler? Changed; }
                class TestClass : IAsyncDisposable {
                    private readonly Source _source = new();
                    public TestClass() { _source.Changed += OnChanged; }
                    private void OnChanged(object? sender, EventArgs e) { }
                    public async ValueTask DisposeAsync() {
                        _source.Changed -= OnChanged;
                        await Task.CompletedTask;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task MultipleSubscriptions_AllNotCancelled_ReportsAll() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Source {
                    public event EventHandler? Changed;
                    public event EventHandler? Created;
                }
                class TestClass : IDisposable {
                    private readonly Source _source = new();
                    public TestClass() {
                        {|#0:_source.Changed += OnChanged|};
                        {|#1:_source.Created += OnCreated|};
                    }
                    private void OnChanged(object? sender, EventArgs e) { }
                    private void OnCreated(object? sender, EventArgs e) { }
                    public void Dispose() { }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("_source", "Changed"),
                new DiagnosticResult("JCC9308", DiagnosticSeverity.Warning).WithLocation(1).WithArguments("_source", "Created"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldEvent_ConditionalAccessCancel_InDisposeAsync_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState = { ParseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview) },
            TestCode = """
                #nullable disable
                using System;
                using System.Threading.Tasks;
                class Source { public event EventHandler? AfterRestart; }
                class TestClass : IAsyncDisposable {
                    private Source _source;
                    public TestClass() { _source = new Source(); _source.AfterRestart += OnRestarted; }
                    private void OnRestarted(object? sender, EventArgs e) { }
                    public async ValueTask DisposeAsync() { _source?.AfterRestart -= OnRestarted; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NestedClass_EventSubscription_CancelledInNestedDispose_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<MemoryLeakRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                class Outer { public event EventHandler? MessageProcessed; }
                class OuterClass {
                    private sealed class InnerScope : IDisposable {
                        private readonly Outer _client;
                        private readonly EventHandler _handler;
                        public InnerScope(Outer client) {
                            _client = client;
                            _handler = (s, e) => { };
                            _client.MessageProcessed += _handler;
                        }
                        public void Dispose() { _client.MessageProcessed -= _handler; }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
