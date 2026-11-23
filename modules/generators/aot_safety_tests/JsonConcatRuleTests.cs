namespace AotSafety.Tests;

public class JsonConcatRuleTests {
    [Fact]
    public async Task EscapeJsonStringCall_ReportsJCC1017() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo
                {
                    string Bar(string s)
                    {
                        return {|JCC1017:EscapeJsonString(s)|};
                    }
                    string EscapeJsonString(string s) => s;
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StringBuilderAppendJsonLiteral_ReportsJCC1017() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Text;
                class Foo
                {
                    string Bar()
                    {
                        var sb = new StringBuilder();
                        {|JCC1017:sb.Append("{\"name\":\"")|};
                        return sb.ToString();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InterpolatedStringJsonBraces_ReportsJCC1017() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo
                {
                    string Bar(string name, string value)
                    {
                        return {|JCC1017:$"{{\"name\":\"{name}\",\"value\":\"{value}\"}}"|};
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StringBuilderAppendNonJson_NoReport() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Text;
                class Foo
                {
                    string Bar()
                    {
                        var sb = new StringBuilder();
                        sb.Append("hello world");
                        sb.AppendLine("test");
                        return sb.ToString();
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InterpolatedStringNonJson_NoReport() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class Foo
                {
                    string Bar(string name, int age)
                    {
                        return $"Name: {name}, Age: {age}";
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task JsonSerializerSerialize_NoReport() {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Text.Json;
                class Foo
                {
                    string Bar()
                    {
                #pragma warning disable JCC1011
                        return JsonSerializer.Serialize(new { name = "test" });
                #pragma warning restore JCC1011
                    }
                }
                """,
        };
        await test.RunAsync().ConfigureAwait(true);
    }
}
