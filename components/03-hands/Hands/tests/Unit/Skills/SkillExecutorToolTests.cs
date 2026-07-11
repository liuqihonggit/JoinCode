
namespace Core.Tests.Skills;

/// <summary>
/// SkillExecutor 工具调用测试类
/// 测试真实工具调用流程、参数传递和结果处理
/// </summary>
public class SkillExecutorToolTests
{
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;
    private readonly SkillExecutor _skillExecutor;

    public SkillExecutorToolTests()
    {
        _queryEngineMock = new Mock<IQueryEngine>();
        _toolRegistryMock = new Mock<IToolRegistry>();
        _skillExecutor = new SkillExecutor(
            _queryEngineMock.Object,
            _toolRegistryMock.Object,
            NullLogger<SkillExecutor>.Instance);
    }

    #region 工具调用流程测试

    /// <summary>
    /// 测试工具步骤应该调用 ToolRegistry
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithToolStep_ShouldCallToolRegistry()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "TestTool",
                    Prompt = "{\"param\": \"value\"}"
                }
            }
        };

        var toolResult = new ToolResult
        {
            IsError = false,
            Content = new List<ToolContent>
            {
                new() { Type = ToolContentType.Text, Text = "Tool executed successfully" }
            }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                "TestTool",
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(toolResult);

        var result = await _skillExecutor.ExecuteAsync(skill, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Contain("Tool executed successfully");
        _toolRegistryMock.Verify(
            x => x.ExecuteToolAsync(
                "TestTool",
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()),
            Times.Once);
    }

    /// <summary>
    /// 测试工具参数应该正确传递
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldPassCorrectToolParameters()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "TestTool",
                    Prompt = "{\"name\": \"John\", \"age\": 30}"
                }
            }
        };

        Dictionary<string, JsonElement>? capturedArgs = null;
        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .Callback<string, Dictionary<string, JsonElement>, CancellationToken, ToolProgressCallback?>((_, args, _, _) =>
            {
                capturedArgs = args;
            })
            .ReturnsAsync(new ToolResult
            {
                IsError = false,
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "OK" } }
            });

        await _skillExecutor.ExecuteAsync(skill, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        capturedArgs.Should().NotBeNull();
        capturedArgs.Should().ContainKey("name");
        capturedArgs.Should().ContainKey("age");
    }

    /// <summary>
    /// 测试工具错误应该被正确处理
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithToolError_ShouldHandleError()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "FailingTool",
                    Prompt = "{}"
                }
            }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult
            {
                IsError = true,
                Content = new List<ToolContent>
                {
                    new() { Type = ToolContentType.Text, Text = "Tool execution failed" }
                }
            });

        var result = await _skillExecutor.ExecuteAsync(skill, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Tool execution failed");
    }

    #endregion

    #region 变量替换测试

    /// <summary>
    /// 测试工具参数中的变量应该被替换
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldReplaceVariablesInToolParameters()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Parameters = new Dictionary<string, SkillParameter>
            {
                { "userName", new SkillParameter { Type = "string", Description = "User name parameter", Required = true } }
            },
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "GreetTool",
                    Prompt = "{\"name\": \"{{userName}}\"}"
                }
            }
        };

        Dictionary<string, JsonElement>? capturedArgs = null;
        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .Callback<string, Dictionary<string, JsonElement>, CancellationToken, ToolProgressCallback?>((_, args, _, _) =>
            {
                capturedArgs = args;
            })
            .ReturnsAsync(new ToolResult
            {
                IsError = false,
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Hello" } }
            });

        var parameters = new Dictionary<string, JsonElement>
        {
            { "userName", JsonSerializer.SerializeToElement("Alice") }
        };

        await _skillExecutor.ExecuteAsync(skill, parameters).ConfigureAwait(true);

        capturedArgs.Should().NotBeNull();
        var nameValue = capturedArgs!["name"].GetString();
        nameValue.Should().Be("Alice");
    }

    #endregion

    #region 工具结果处理测试

    /// <summary>
    /// 测试多内容结果应该被正确合并
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithMultiContentResult_ShouldMergeContent()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "MultiContentTool",
                    Prompt = "{}"
                }
            }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult
            {
                IsError = false,
                Content = new List<ToolContent>
                {
                    new() { Type = ToolContentType.Text, Text = "Line 1" },
                    new() { Type = ToolContentType.Text, Text = "Line 2" },
                    new() { Type = ToolContentType.Text, Text = "Line 3" }
                }
            });

        var result = await _skillExecutor.ExecuteAsync(skill, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Contain("Line 1");
        result.Output.Should().Contain("Line 2");
        result.Output.Should().Contain("Line 3");
    }

    /// <summary>
    /// 测试空内容结果应该返回空字符串
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithEmptyContent_ShouldReturnEmptyString()
    {
        var skill = new SkillDefinition
        {
            Name = "TestSkill",
            Description = "Test skill description",
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Tool,
                    Tool = "EmptyTool",
                    Prompt = "{}"
                }
            }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult
            {
                IsError = false,
                Content = new List<ToolContent>()
            });

        var result = await _skillExecutor.ExecuteAsync(skill, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().BeEmpty();
    }

    #endregion
}
