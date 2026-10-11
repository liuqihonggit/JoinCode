namespace AotSafety.Tests;

public class NullForgivingOperatorRuleTests {
    [Fact]
    public async Task NullForgivingOnVariable_ReportsJCC11003() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    void Bar(string? x)
                    {
                        string y = x{|JCC11003:!|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NullForgivingOnNullLiteral_ReportsJCC11003() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    void Bar()
                    {
                        string y = null{|JCC11003:!|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NoNullForgiving_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    void Bar(string x)
                    {
                        string y = x;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PragmaSuppress_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    void Bar(string? x)
                    {
                #pragma warning disable JCC11003
                        string y = x!;
                #pragma warning restore JCC11003
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
