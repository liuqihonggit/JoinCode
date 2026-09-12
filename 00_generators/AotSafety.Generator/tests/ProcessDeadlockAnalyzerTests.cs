namespace AotSafety.Tests;

public class ProcessDeadlockAnalyzerTests
{
    [Fact]
    public async Task WaitForExitAsync_BeforeReadToEndAsync_ReportsJCC3003()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                using System.Threading;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task RunAsync(CancellationToken ct)
                    {
                        var process = new Process();
                        process.Start();
                        await process.WaitForExitAsync(ct).ConfigureAwait(false);
                        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
                """
        };

        test.ExpectedDiagnostics.Add(
            DiagnosticResult.CompilerWarning("JCC3003").WithSpan(11, 28, 11, 67));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadToEndAsync_BeforeWaitForExitAsync_NoWarning()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                using System.Threading;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task RunAsync(CancellationToken ct)
                    {
                        var process = new Process();
                        process.Start();
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync(ct).ConfigureAwait(false);
                        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                    }
                }
                """
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RedirectStderrTrue_Initializer_ButNeverRead_ReportsJCC3004()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                class TestClass
                {
                    void Run()
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "git",
                            RedirectStandardError = true
                        };
                    }
                }
                """
        };

        test.ExpectedDiagnostics.Add(
            DiagnosticResult.CompilerWarning("JCC3004").WithSpan(6, 19, 10, 10));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RedirectStderrTrue_PropertyAssign_ButNeverRead_ReportsJCC3004()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                class TestClass
                {
                    void Run()
                    {
                        var psi = new ProcessStartInfo();
                        psi.RedirectStandardError = true;
                    }
                }
                """
        };

        test.ExpectedDiagnostics.Add(
            DiagnosticResult.CompilerWarning("JCC3004").WithSpan(6, 19, 6, 41));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RedirectStderrTrue_AndReadStderr_NoWarning()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                using System.Threading.Tasks;
                class TestClass
                {
                    async Task RunAsync()
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "git",
                            RedirectStandardError = true
                        };
                        var process = new Process();
                        process.StartInfo = psi;
                        process.Start();
                        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
                """
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NoRedirectStderr_NoWarning()
    {
        var test = new CSharpAnalyzerTest<AsyncSafetyRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Diagnostics;
                class TestClass
                {
                    void Run()
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "git",
                            RedirectStandardOutput = true
                        };
                    }
                }
                """
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
