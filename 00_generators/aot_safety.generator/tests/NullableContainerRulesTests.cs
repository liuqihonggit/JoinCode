namespace AotSafety.Tests;

public class NullableContainerRulesTests
{
    [Fact]
    public async Task NullableContainerField_ReportsJCC11002()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<string>? {|JCC11002:_items|};
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonNullableContainerField_NoReport()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<string> _items = new();
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InterfaceNullableProperty_NoReport()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                interface IFoo
                {
                    List<string>? Items { get; set; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RecordNullableProperty_NoReport()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                record Foo(List<string>? Items);
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RequiredNullableProperty_NoReport()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public required List<string>? Items { get; init; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NullableDictionaryField_ReportsJCC11002()
    {
        var test = new CSharpAnalyzerTest<NullableContainerRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private Dictionary<string, int>? {|JCC11002:_map|};
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
