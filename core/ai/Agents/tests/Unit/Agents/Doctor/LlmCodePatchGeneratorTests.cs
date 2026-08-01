namespace Core.Tests.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

public class LlmCodePatchGeneratorTests
{
    [Fact]
    public async Task GeneratePatchAsync_ReturnsCodePatchWithTargetFile()
    {
        var generator = CreateGenerator("```csharp\nusing System;\n\nclass Fix { }\n```\n```reasoning\nFixed the bug\n```");

        var diagnostic = CreateDiagnostic();
        var sourceContext = new SourceCodeContext
        {
            FilePath = "/src/FileReadMiddleware.cs",
            CurrentContent = "class Original { }"
        };

        var patch = await generator.GeneratePatchAsync(diagnostic, sourceContext);

        Assert.Equal("/src/FileReadMiddleware.cs", patch.TargetFilePath);
        Assert.NotEmpty(patch.PatchedContent);
    }

    [Fact]
    public async Task GeneratePatchAsync_ExtractsCodeBlockFromResponse()
    {
        var response = "Here is the fix:\n```csharp\nusing System;\n\nclass Fixed { }\n```\n```reasoning\nChanged X to Y\n```";
        var generator = CreateGenerator(response);

        var diagnostic = CreateDiagnostic();
        var sourceContext = new SourceCodeContext
        {
            FilePath = "/src/Foo.cs",
            CurrentContent = "class Foo { }"
        };

        var patch = await generator.GeneratePatchAsync(diagnostic, sourceContext);

        Assert.Contains("class Fixed", patch.PatchedContent);
        Assert.DoesNotContain("```csharp", patch.PatchedContent);
    }

    [Fact]
    public async Task GeneratePatchAsync_WithHistoricalPatches_IncludesInPrompt()
    {
        var capture = new CapturedString();
        var generator = new LlmCodePatchGenerator(prompt =>
        {
            capture.Value = prompt;
            return Task.FromResult("```csharp\nclass X { }\n```\n```reasoning\nfix\n```");
        });

        var diagnostic = CreateDiagnostic();
        var sourceContext = new SourceCodeContext
        {
            FilePath = "/src/Foo.cs",
            CurrentContent = "class Foo { }"
        };
        var historical = new List<CodePatch>
        {
            new() { TargetFilePath = "/src/Foo.cs", PatchedContent = "class Fixed { }", Description = "Previous fix", Confidence = 0.8 }
        };

        await generator.GeneratePatchAsync(diagnostic, sourceContext, historical);

        Assert.Contains("Previous fix", capture.Value);
    }

    [Fact]
    public async Task GeneratePatchAsync_NoCodeBlock_ReturnsRawContent()
    {
        var generator = CreateGenerator("No code block here, just text");

        var diagnostic = CreateDiagnostic();
        var sourceContext = new SourceCodeContext
        {
            FilePath = "/src/Foo.cs",
            CurrentContent = "class Foo { }"
        };

        var patch = await generator.GeneratePatchAsync(diagnostic, sourceContext);

        Assert.Equal("No code block here, just text", patch.PatchedContent);
    }

    [Fact]
    public async Task GeneratePatchAsync_ExtractsReasoning()
    {
        var generator = CreateGenerator("```csharp\nclass X { }\n```\n```reasoning\nRoot cause was null ref\n```");

        var diagnostic = CreateDiagnostic();
        var sourceContext = new SourceCodeContext
        {
            FilePath = "/src/Foo.cs",
            CurrentContent = "class Foo { }"
        };

        var patch = await generator.GeneratePatchAsync(diagnostic, sourceContext);

        Assert.Contains("null ref", patch.Reasoning);
    }

    private static LlmCodePatchGenerator CreateGenerator(string llmResponse)
    {
        return new LlmCodePatchGenerator(_ => Task.FromResult(llmResponse));
    }

    private static DiagnosticReport CreateDiagnostic()
    {
        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.LoopDetected,
            Severity = DiagnosticSeverity.Warning,
            Description = "检测到循环 3 次"
        };
    }

    private sealed class CapturedString
    {
        public string Value { get; set; } = "";
    }
}
