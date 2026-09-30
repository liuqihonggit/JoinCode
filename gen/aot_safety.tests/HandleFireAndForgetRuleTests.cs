namespace AotSafety.Tests;

public class HandleFireAndForgetRuleTests {
    [Fact]
    public async Task Handle_FireAndForget_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;

                class TestActor : ActorBase {
                    protected override void Handle(string cmd, CancellationToken ct) {
                        _ = {|#0:DoAsync()|};
                    }
                    private async Task DoAsync() { await Task.Delay(1); }
                }
                abstract class ActorBase {
                    protected abstract void Handle(string cmd, CancellationToken ct);
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("TestActor.DoAsync"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Handle_RegisterInFlight_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;

                class TestActor : ActorBase {
                    protected override void Handle(string cmd, CancellationToken ct) {
                        RegisterInFlight(DoAsync());
                    }
                    private async Task DoAsync() { await Task.Delay(1); }
                    protected void RegisterInFlight(Task t) { }
                }
                abstract class ActorBase {
                    protected abstract void Handle(string cmd, CancellationToken ct);
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Handle_SyncOnly_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;

                class TestActor : ActorBase {
                    protected override void Handle(string cmd, CancellationToken ct) {
                        Console.WriteLine(cmd);
                    }
                }
                abstract class ActorBase {
                    protected abstract void Handle(string cmd, CancellationToken ct);
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Handle_BareInvocation_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;

                class TestActor : ActorBase {
                    protected override void Handle(string cmd, CancellationToken ct) {
                        {|#0:DoAsync()|};
                    }
                    private async Task DoAsync() { await Task.Delay(1); }
                }
                abstract class ActorBase {
                    protected abstract void Handle(string cmd, CancellationToken ct);
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("TestActor.DoAsync"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Handle_BareTaskRun_ReportsJCC9305() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;

                class TestActor : ActorBase {
                    protected override void Handle(string cmd, CancellationToken ct) {
                        {|#0:Task.Run(async () => { await Task.Delay(1); })|};
                    }
                }
                abstract class ActorBase {
                    protected abstract void Handle(string cmd, CancellationToken ct);
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC9305", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("Task.Run"),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
