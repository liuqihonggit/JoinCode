namespace AotSafety.Tests;

public class JsonSerializerAotRulesTests
{
    private static ReferenceAssemblies GetReferences()
    {
        return ReferenceAssemblies.Net.Net90;
    }

    [Fact]
    public async Task SerializeToElement_WithoutTypeInfo_ReportsJCC1011()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                using System.Collections.Generic;
                
                class TestClass
                {
                    void M()
                    {
                        var dict = new Dictionary<string, JsonElement>();
                        dict["key"] = {|#0:JsonSerializer.SerializeToElement("hello")|};
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1011", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("SerializeToElement"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Deserialize_WithoutTypeInfo_ReportsJCC1011()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                
                class TestClass
                {
                    void M()
                    {
                        var result = {|#0:JsonSerializer.Deserialize<string>("{}")|};
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1011", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("Deserialize"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NewJsonSerializerOptions_WithoutResolver_ReportsJCC1012()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                
                class TestClass
                {
                    static readonly JsonSerializerOptions s_options = {|#0:new JsonSerializerOptions { WriteIndented = true }|};
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1012", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("WriteIndented"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NewJsonSerializerOptions_Empty_ReportsJCC1012()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                
                class TestClass
                {
                    void M()
                    {
                        var options = {|#0:new JsonSerializerOptions()|};
                    }
                }
                """,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1012", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("无初始化器"),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SerializeToElement_JsonElementArg_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                
                class TestClass
                {
                    JsonElement M(JsonElement input)
                    {
                        return JsonSerializer.SerializeToElement(input);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SerializeToElement_JsonNodeArg_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<JsonSerializerAotRules, DefaultVerifier>
        {
            ReferenceAssemblies = GetReferences(),
            TestCode = """
                using System.Text.Json;
                using System.Text.Json.Nodes;
                
                class TestClass
                {
                    JsonElement M(JsonNode node)
                    {
                        return JsonSerializer.SerializeToElement(node);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
