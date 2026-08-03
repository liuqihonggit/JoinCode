
namespace Core.Tests.Skills;

public class SkillExecutorTests
{
    [Fact]
    public void SkillDefinition_DefaultVersion_ShouldBe1_0()
    {
        var skill = new SkillDefinition
        {
            Name = "test",
            Description = "Test",
            Steps = new List<SkillStep>()
        };

        skill.Version.Should().Be("1.0");
    }

    [Fact]
    public void SkillDefinition_DefaultTimeout_ShouldBe300()
    {
        var skill = new SkillDefinition
        {
            Name = "test",
            Description = "Test",
            Steps = new List<SkillStep>()
        };

        skill.TimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void SkillParameter_DefaultRequired_ShouldBeTrue()
    {
        var param = new SkillParameter
        {
            Type = "string",
            Description = "Test parameter"
        };

        param.Required.Should().BeTrue();
    }

    [Fact]
    public void SkillStep_DefaultDescription_ShouldBeEmpty()
    {
        var step = new SkillStep
        {
            Id = "step1",
            Type = SkillStepType.Prompt
        };

        step.Description.Should().BeEmpty();
    }

    [Fact]
    public void LoopConfig_Properties_ShouldBeSettable()
    {
        var loop = new LoopConfig
        {
            Count = 5,
            Condition = "true",
            Variable = "item"
        };

        loop.Count.Should().Be(5);
        loop.Condition.Should().Be("true");
        loop.Variable.Should().Be("item");
    }

    [Fact]
    public void SkillExecutionResult_DefaultValues_ShouldBeSet()
    {
        var result = new SkillExecutionResult
        {
            SkillName = "test",
            Output = "output"
        };

        result.SkillName.Should().Be("test");
        result.Output.Should().Be("output");
        result.IsSuccess.Should().BeFalse();
        result.StepResults.Should().BeEmpty();
    }

    [Fact]
    public void StepResult_DefaultValues_ShouldBeSet()
    {
        var result = new StepResult
        {
            StepId = "step1"
        };

        result.StepId.Should().Be("step1");
        result.IsSuccess.Should().BeFalse();
        result.Output.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void SkillDefinition_WithParameters_ShouldStoreParameters()
    {
        var skill = new SkillDefinition
        {
            Name = "param_test",
            Description = "Test with parameters",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["required_param"] = new SkillParameter
                {
                    Type = "string",
                    Description = "Required parameter",
                    Required = true
                },
                ["optional_param"] = new SkillParameter
                {
                    Type = "integer",
                    Description = "Optional parameter",
                    Required = false,
                    DefaultValue = 42
                }
            },
            Steps = new List<SkillStep>()
        };

        skill.Parameters.Should().HaveCount(2);
        skill.Parameters["required_param"].Required.Should().BeTrue();
        skill.Parameters["optional_param"].Required.Should().BeFalse();
        skill.Parameters["optional_param"].DefaultValue.Should().Be(42);
    }

    [Fact]
    public void SkillDefinition_WithSteps_ShouldStoreSteps()
    {
        var skill = new SkillDefinition
        {
            Name = "step_test",
            Description = "Test with steps",
            Parameters = new Dictionary<string, SkillParameter>(),
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = FileToolNameConstants.FileRead,
                    Description = "Read a file",
                    Next = "step2"
                },
                new()
                {
                    Id = "step2",
                    Type = SkillStepType.Prompt,
                    Prompt = "Analyze the content",
                    Next = null
                }
            }
        };

        skill.Steps.Should().HaveCount(2);
        skill.Steps[0].Next.Should().Be("step2");
        skill.Steps[1].Next.Should().BeNull();
    }

    [Fact]
    public void SkillStep_WithLoopConfig_ShouldStoreLoop()
    {
        var step = new SkillStep
        {
            Id = "loop_step",
            Type = SkillStepType.Loop,
            Description = "Loop through items",
            Loop = new LoopConfig
            {
                Count = 10,
                Variable = "item"
            },
            Next = "complete"
        };

        step.Loop.Should().NotBeNull();
        step.Loop!.Count.Should().Be(10);
        step.Loop.Variable.Should().Be("item");
    }

    [Fact]
    public void SkillStep_WithCondition_ShouldStoreCondition()
    {
        var step = new SkillStep
        {
            Id = "condition_step",
            Type = SkillStepType.Condition,
            Condition = "{{should_continue}}",
            Next = "next_step",
            OnError = "error_handler"
        };

        step.Condition.Should().Be("{{should_continue}}");
        step.Next.Should().Be("next_step");
        step.OnError.Should().Be("error_handler");
    }

    [Fact]
    public void SkillDefinition_RequiresConfirmation_ShouldBeConfigurable()
    {
        var skill = new SkillDefinition
        {
            Name = "dangerous",
            Description = "Dangerous operation",
            Parameters = new Dictionary<string, SkillParameter>(),
            Steps = new List<SkillStep>(),
            RequiresConfirmation = true
        };

        skill.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public void SkillDefinition_WithFrontmatterMetadata_ShouldStoreMetadata()
    {
        var skill = new SkillDefinition
        {
            Name = "metadata_test",
            Description = "Test metadata",
            Steps = new List<SkillStep>(),
            Author = "Test Author",
            Version = "2.0",
            Tags = new[] { "test", "demo" },
            Permissions = new[] { FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite },
            Dependencies = new[] { "dep1", "dep2" },
            Namespace = "test.group"
        };

        skill.Author.Should().Be("Test Author");
        skill.Version.Should().Be("2.0");
        skill.Tags.Should().HaveCount(2);
        skill.Tags.Should().Contain("test");
        skill.Tags.Should().Contain("demo");
        skill.Permissions.Should().Contain(FileToolNameConstants.FileRead);
        skill.Permissions.Should().Contain(FileToolNameConstants.FileWrite);
        skill.Dependencies.Should().Contain("dep1");
        skill.Dependencies.Should().Contain("dep2");
        skill.Namespace.Should().Be("test.group");
    }

    [Fact]
    public void SkillDefinition_WithSourceInfo_ShouldTrackSource()
    {
        var lastModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var skill = new SkillDefinition
        {
            Name = "source_test",
            Description = "Test source tracking",
            Steps = new List<SkillStep>(),
            SourcePath = "/path/to/skill.md",
            SourceFormat = SkillSourceFormat.Markdown,
            LastModified = lastModified
        };

        skill.SourcePath.Should().Be("/path/to/skill.md");
        skill.SourceFormat.Should().Be(SkillSourceFormat.Markdown);
        skill.LastModified.Should().Be(lastModified);
    }

    [Fact]
    public void SkillDefinition_WithContentTemplate_ShouldStoreTemplate()
    {
        var skill = new SkillDefinition
        {
            Name = "template_test",
            Description = "Test content template",
            Steps = new List<SkillStep>(),
            ContentTemplate = "# {{title}}\n\nThis is a template with {{variable}}."
        };

        skill.ContentTemplate.Should().Be("# {{title}}\n\nThis is a template with {{variable}}.");
    }

    [Fact]
    public void SkillDefinition_WithExtraMetadata_ShouldStoreExtra()
    {
        var skill = new SkillDefinition
        {
            Name = "extra_test",
            Description = "Test extra metadata",
            Steps = new List<SkillStep>(),
            Extra = new Dictionary<string, JsonElement>
            {
                ["custom_field"] = JsonSerializer.SerializeToElement("value", SkillsJsonContext.Default.String),
                ["count"] = JsonSerializer.SerializeToElement(42, SkillsJsonContext.Default.Int32)
            }
        };

        skill.Extra.Should().ContainKey("custom_field");
        skill.Extra["custom_field"].GetString().Should().Be("value");
    }

    [Fact]
    public void SkillParameter_WithValidation_ShouldStoreValidation()
    {
        var param = new SkillParameter
        {
            Type = "string",
            Description = "Test with validation",
            Validation = new ParameterValidation
            {
                MinLength = 5,
                MaxLength = 100,
                Pattern = "^[a-zA-Z]+$",
                EnumValues = new[] { "option1", "option2" }
            }
        };

        param.Validation.Should().NotBeNull();
        param.Validation!.MinLength.Should().Be(5);
        param.Validation.MaxLength.Should().Be(100);
        param.Validation.Pattern.Should().Be("^[a-zA-Z]+$");
        param.Validation.EnumValues.Should().HaveCount(2);
    }

    [Fact]
    public void SkillStep_WithBranches_ShouldStoreBranches()
    {
        var step = new SkillStep
        {
            Id = "branch_step",
            Type = SkillStepType.Condition,
            Description = "Conditional branching",
            Branches = new Dictionary<string, List<SkillStep>>
            {
                ["if_true"] = new List<SkillStep>
                {
                    new() { Id = "true_step", Type = SkillStepType.Prompt, Prompt = "True branch" }
                },
                ["if_false"] = new List<SkillStep>
                {
                    new() { Id = "false_step", Type = SkillStepType.Prompt, Prompt = "False branch" }
                }
            }
        };

        step.Branches.Should().NotBeNull();
        step.Branches.Should().ContainKey("if_true");
        step.Branches.Should().ContainKey("if_false");
        step.Branches["if_true"].Should().HaveCount(1);
    }

    [Fact]
    public void SkillStep_WithTimeout_ShouldStoreTimeout()
    {
        var step = new SkillStep
        {
            Id = "timeout_step",
            Type = SkillStepType.Tool,
            Tool = "shell",
            TimeoutSeconds = 60
        };

        step.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void SkillStep_WithRetry_ShouldStoreRetryConfig()
    {
        var step = new SkillStep
        {
            Id = "retry_step",
            Type = SkillStepType.Tool,
            Tool = "http",
            Retry = new JoinCode.Abstractions.Models.Skill.RetryConfig
            {
                MaxAttempts = 3,
                DelayMs = 1000,
                ExponentialBackoff = true
            }
        };

        step.Retry.Should().NotBeNull();
        step.Retry!.MaxAttempts.Should().Be(3);
        step.Retry.DelayMs.Should().Be(1000);
        step.Retry.ExponentialBackoff.Should().BeTrue();
    }

    [Fact]
    public void LoopConfig_WithBody_ShouldStoreBody()
    {
        var loop = new LoopConfig
        {
            Variable = "item",
            Body = new List<SkillStep>
            {
                new() { Id = "process_item", Type = SkillStepType.Prompt, Prompt = "Process {{item}}" }
            },
            MaxIterations = 50
        };

        loop.Body.Should().NotBeNull();
        loop.Body.Should().HaveCount(1);
        loop.MaxIterations.Should().Be(50);
    }

    [Fact]
    public void SkillDefinition_FluentMethods_ShouldWork()
    {
        var skill = new SkillDefinition
        {
            Name = "fluent_test",
            Description = "Test fluent methods",
            Steps = new List<SkillStep>()
        };

        var withPath = skill with { SourcePath = "/path/to/skill.md" };
        var withFormat = withPath with { SourceFormat = SkillSourceFormat.Markdown };
        var withModified = withFormat with { LastModified = DateTime.UtcNow };
        var withNamespace = withModified with { Namespace = "test.fluent" };

        withNamespace.Name.Should().Be("fluent_test");
        withNamespace.SourcePath.Should().Be("/path/to/skill.md");
        withNamespace.SourceFormat.Should().Be(SkillSourceFormat.Markdown);
        withNamespace.Namespace.Should().Be("test.fluent");
    }
}
