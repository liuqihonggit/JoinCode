namespace AotSafety.Tests;

public class DisposeTimeoutWaitRuleTests {
    [Fact]
    public async Task DisposeWithWait_ReportsJCC9203() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public void Dispose()
                    {
                        {|JCC9203:_task.Wait()|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeWithWaitTimeout_ReportsJCC9203() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public void Dispose()
                    {
                        {|JCC9203:_task.Wait(TimeSpan.FromSeconds(5))|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsyncWithGetAwaiterGetResult_ReportsJCC9203() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public ValueTask DisposeAsync()
                    {
                        {|JCC9203:_task.GetAwaiter().GetResult()|};
                        return ValueTask.CompletedTask;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeWithTaskWhenAnyDelay_ReportsJCC9203() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public void Dispose()
                    {
                        {|JCC9203:Task.WhenAny(_task, Task.Delay(5000))|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeWithAwait_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public async ValueTask DisposeAsync()
                    {
                        await _task.ConfigureAwait(false);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonDisposeMethodWithWait_NoReport() {
        var test = new CSharpAnalyzerTest<DisposableConsistencyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Threading.Tasks;
                class Foo
                {
                    private Task _task = Task.CompletedTask;
                    public void Run()
                    {
                        _task.Wait();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
