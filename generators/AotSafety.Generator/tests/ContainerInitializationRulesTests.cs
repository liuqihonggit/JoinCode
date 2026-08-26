namespace AotSafety.Tests;

public class ContainerInitializationRulesTests
{
    [Fact]
    public async Task ContainerField_NotInitialized_ReportsJCC11001()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<string> {|JCC11001:_items|};
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ContainerField_Initialized_NoReport()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
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
    public async Task NullableContainerField_NoReport()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private List<string>? _items;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConstructorAssignedField_NoReport()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    private readonly List<string> _items;
                    public Foo() { _items = new(); }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InterfaceProperty_NoReport()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                interface IFoo
                {
                    List<string> Items { get; set; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RequiredInitProperty_NoReport()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public required List<string> Items { get; init; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SettableProperty_NotInitialized_ReportsJCC11001()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class Foo
                {
                    public List<string> {|JCC11001:Items|} { get; set; }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ArrayField_NotInitialized_ReportsJCC11001()
    {
        var test = new CSharpAnalyzerTest<ContainerInitializationRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo
                {
                    private string[] {|JCC11001:_names|};
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
