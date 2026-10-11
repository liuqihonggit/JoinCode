namespace AotSafety.Tests;

public class PithosDbSafetyRulesTests {
    [Fact]
    public async Task SSTableReader_WithMemoryMappedFile_ReportsJCC9310() {
        var test = new CSharpAnalyzerTest<PithosDbSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.IO;
                using System.IO.MemoryMappedFiles;
                namespace PithosDB.Core.Storage;

                public sealed class SSTableReader
                {
                    private readonly MemoryMappedFile _mmf;

                    public SSTableReader(string path)
                    {
                        _mmf = MemoryMappedFile.{|#0:CreateFromFile|}(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9310", DiagnosticSeverity.Error).WithLocation(0),
            },
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OtherClass_WithMemoryMappedFile_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<PithosDbSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.IO;
                using System.IO.MemoryMappedFiles;

                public sealed class OtherReader
                {
                    private readonly MemoryMappedFile _mmf;

                    public OtherReader(string path)
                    {
                        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SSTableReader_WithoutMemoryMappedFile_NoDiagnostic() {
        var test = new CSharpAnalyzerTest<PithosDbSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Runtime.InteropServices;

                namespace PithosDB.Core.Storage;

                public sealed class SSTableReader
                {
                    private readonly byte[] _buffer;
                    private readonly GCHandle _handle;

                    public SSTableReader(string path)
                    {
                        _buffer = new byte[1024];
                        _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
