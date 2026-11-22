namespace AotSafety.Tests;

public class TupleDictionaryKeyRuleTests {
    [Fact]
    public async Task DictionaryWithTupleKey_ReportsJCC10009() {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private {|JCC10009:Dictionary<(string, int), string>|} _map = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DictionaryWithStringKey_NoReport() {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private Dictionary<string, string> _map = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConcurrentDictionaryWithTupleKey_ReportsJCC10009() {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Concurrent;
                class Foo
                {
                    private {|JCC10009:ConcurrentDictionary<(int, int), string>|} _map = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DictionaryWithThreeTupleKey_ReportsJCC10009() {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private {|JCC10009:Dictionary<(string, int, bool), string>|} _map = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonDictionaryWithTupleKey_NoReport() {
        var test = new CSharpAnalyzerTest<CodeOrganizationRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<(string, int)> _list = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
