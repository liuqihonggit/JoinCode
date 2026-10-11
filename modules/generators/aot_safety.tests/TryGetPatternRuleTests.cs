namespace AotSafety.Tests;

public class TryGetPatternRuleTests {
    [Fact]
    public async Task PublicMethodReturnsNullRef_ReportsJCC11005() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    public string? {|JCC11005:GetName|}()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TryGetMethod_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    public string? TryGetName()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task MethodWithOutParam_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    public string? GetName(out int x)
                    {
                        x = 0;
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PrivateMethodReturnsNull_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    private string? GetName()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodReturnsNonNull_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    public string? GetName()
                    {
                        return "hello";
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodReturnsNullableInt_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                #nullable enable
                class Foo
                {
                    public int? GetAge()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
