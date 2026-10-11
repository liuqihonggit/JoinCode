namespace AotSafety.Tests;

public class PublicApiReturnsNullCollectionRuleTests {
    [Fact]
    public async Task PublicMethodReturnsNullList_ReportsJCC11004() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public List<string> {|JCC11004:GetItems|}()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodReturnsEmptyList_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public List<string> GetItems()
                    {
                        return new List<string>();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PrivateMethodReturnsNullList_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<string> GetItems()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodReturnsNullString_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo
                {
                    public string GetName()
                    {
                        return null;
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicPropertyReturnsNullList_ReportsJCC11004() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public List<string> {|JCC11004:Items|}
                    {
                        get { return null; }
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodReturnsNullInLambda_NoReport() {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System;
                using System.Collections.Generic;
                class Foo
                {
                    public List<string> GetItems()
                    {
                        Func<List<string>> f = () => null;
                        return new List<string>();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
