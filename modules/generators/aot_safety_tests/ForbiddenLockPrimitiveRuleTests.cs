namespace AotSafety.Tests;

public class ForbiddenLockPrimitiveRuleTests {
    [Fact]
    public async Task LockStatement_ReportsJCC4008() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    private readonly object _gate = new();

                    void Run()
                    {
                        {|#0:lock|}(_gate)
                        {
                        }
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC4008", DiagnosticSeverity.Error)
                    .WithLocation(0)
                    .WithArguments("lock 语句", "改用 Actor 单写者邮箱（Channel<T>）或不可变快照 + Interlocked.CompareExchange"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SemaphoreSlimField_ReportsJCC4008() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading;

                class TestClass
                {
                    private readonly SemaphoreSlim {|#0:_semaphore|} = new(1, 1);
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC4008", DiagnosticSeverity.Error)
                    .WithLocation(0)
                    .WithArguments("SemaphoreSlim", "互斥改用 Actor/Channel; 限流改用令牌状态 + CAS; 信号改用 Channel 或 TaskCompletionSource"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task MonitorInvocation_ReportsJCC4008() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading;

                class TestClass
                {
                    void Run(object gate)
                    {
                        {|#0:Monitor.Enter|}(gate);
                    }
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC4008", DiagnosticSeverity.Error)
                    .WithLocation(0)
                    .WithArguments("Monitor.Enter", "改用 Actor 单写者邮箱（Channel<T>）或不可变快照 + Interlocked.CompareExchange"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ProjectLockType_ReportsJCC4008() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class AsyncLock
                {
                }

                class TestClass
                {
                    private readonly AsyncLock {|#0:_lock|} = new();
                }
                """,
            ExpectedDiagnostics = {
                new DiagnosticResult("JCC4008", DiagnosticSeverity.Error)
                    .WithLocation(0)
                    .WithArguments("AsyncLock", "改用 Actor/Channel 或不可变快照 + CAS; 该锁生态将归档，禁止新增消费"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ActorAndCasAndChannel_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<ConcurrencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Threading;
                using System.Threading.Channels;
                using System.Threading.Tasks;

                class TestClass
                {
                    private int _state;
                    private readonly Channel<int> _mailbox = Channel.CreateUnbounded<int>();

                    void Run()
                    {
                        Interlocked.CompareExchange(ref _state, 1, 0);
                        _mailbox.Writer.TryWrite(1);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
