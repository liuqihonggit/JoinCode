namespace Guard.Tests.Security.Services;

public sealed class AutoModeClassifierTests
{
    private readonly AutoModeClassifier _sut;

    public AutoModeClassifierTests()
    {
        _sut = new AutoModeClassifier(NullLogger<AutoModeClassifier>.Instance);
    }

    [Fact]
    public async Task ClassifyAsync_NullRequest_ShouldThrowArgumentNullException()
    {
        var act = async () => await _sut.ClassifyAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Theory]
    [InlineData(FileToolNameConstants.FileRead)]
    [InlineData("file_list")]
    [InlineData(SearchToolNameConstants.Glob)]
    [InlineData(SearchToolNameConstants.Grep)]
    [InlineData(WebToolNameConstants.WebFetch)]
    [InlineData(WebToolNameConstants.WebSearch)]
    [InlineData(TaskToolNameConstants.TaskList)]
    [InlineData("code_search")]
    [InlineData("symbol_search")]
    public async Task ClassifyAsync_ReadOnlyTools_ShouldReturnSafeAutoApprove(string toolName)
    {
        var request = new ClassificationRequest
        {
            ToolName = toolName,
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Read
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.Safe);
        result.Action.Should().Be(SecurityAction.AutoApprove);
        result.Confidence.Should().BeGreaterThan(0.9);
    }

    [Theory]
    [InlineData(ShellToolNameConstants.ShellExecute)]
    [InlineData(ShellToolNameConstants.Powershell)]
    [InlineData("file_delete")]
    [InlineData("git_reset")]
    [InlineData("git_clean")]
    [InlineData("git_push")]
    public async Task ClassifyAsync_SensitiveTools_ShouldReturnHighRiskRequireApproval(string toolName)
    {
        var request = new ClassificationRequest
        {
            ToolName = toolName,
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Execute
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.HighRisk);
        result.Action.Should().Be(SecurityAction.RequireApproval);
    }

    [Theory]
    [InlineData(FileToolNameConstants.FileWrite)]
    [InlineData(FileToolNameConstants.FileEdit)]
    [InlineData(TodoToolNameConstants.TodoWrite)]
    public async Task ClassifyAsync_SafeWriteTools_ShouldReturnLowRiskAutoApprove(string toolName)
    {
        var request = new ClassificationRequest
        {
            ToolName = toolName,
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Write
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.LowRisk);
        result.Action.Should().Be(SecurityAction.AutoApprove);
    }

    [Fact]
    public async Task ClassifyAsync_ReadOperationType_ShouldReturnSafeAutoApprove()
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Read
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.Safe);
        result.Action.Should().Be(SecurityAction.AutoApprove);
    }

    [Fact]
    public async Task ClassifyAsync_WriteOperationType_ShouldReturnLowRiskAutoApprove()
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Write
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.LowRisk);
        result.Action.Should().Be(SecurityAction.AutoApprove);
    }

    [Fact]
    public async Task ClassifyAsync_DangerousCommand_ShouldReturnDangerousBlock()
    {
        var request = new ClassificationRequest
        {
            ToolName = ShellToolNameConstants.ShellExecute,
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonElementHelper.FromString("rm -rf /") },
            OperationType = OperationType.Execute
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.Dangerous);
        result.Action.Should().Be(SecurityAction.Block);
        result.Confidence.Should().BeGreaterThan(0.95);
    }

    [Theory]
    [InlineData("rm -rf ~")]
    [InlineData("del /f /s /q c:")]
    [InlineData("format C:")]
    [InlineData("shutdown /s /t 0")]
    public async Task ClassifyAsync_VariousDangerousCommands_ShouldReturnDangerousBlock(string command)
    {
        var request = new ClassificationRequest
        {
            ToolName = ShellToolNameConstants.ShellExecute,
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonElementHelper.FromString(command) },
            OperationType = OperationType.Execute
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.Dangerous);
        result.Action.Should().Be(SecurityAction.Block);
    }

    [Fact]
    public async Task ClassifyAsync_SensitivePath_ShouldReturnMediumRiskRequireConfirmation()
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement> { ["path"] = JsonElementHelper.FromString("/home/user/.ssh/id_rsa") },
            OperationType = OperationType.Auto
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.MediumRisk);
        result.Action.Should().Be(SecurityAction.RequireConfirmation);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".git")]
    [InlineData("credentials")]
    [InlineData("secrets")]
    [InlineData("auth")]
    public async Task ClassifyAsync_SensitivePathSegments_ShouldReturnMediumRisk(string segment)
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement> { ["path"] = JsonElementHelper.FromString($"/home/user/{segment}/config") },
            OperationType = OperationType.Auto
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.MediumRisk);
        result.Action.Should().Be(SecurityAction.RequireConfirmation);
    }

    [Fact]
    public async Task ClassifyAsync_UnknownOperationType_ShouldReturnMediumRiskRequireConfirmation()
    {
        var request = new ClassificationRequest
        {
            ToolName = "unknown_tool",
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Auto
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.MediumRisk);
        result.Action.Should().Be(SecurityAction.RequireConfirmation);
        result.Confidence.Should().BeLessThan(0.7);
    }

    [Fact]
    public async Task ClassifyAsync_NonStringCommandParameter_ShouldNotDetectDangerousCommand()
    {
        var request = new ClassificationRequest
        {
            ToolName = ShellToolNameConstants.ShellExecute,
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonElementHelper.FromInt32(12345) },
            OperationType = OperationType.Execute
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.HighRisk);
        result.Action.Should().Be(SecurityAction.RequireApproval);
    }

    [Fact]
    public async Task ClassifyAsync_SafeCommandWithSensitiveTool_ShouldReturnHighRisk()
    {
        var request = new ClassificationRequest
        {
            ToolName = ShellToolNameConstants.ShellExecute,
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonElementHelper.FromString("echo hello") },
            OperationType = OperationType.Execute
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.HighRisk);
        result.Action.Should().Be(SecurityAction.RequireApproval);
    }

    [Fact]
    public async Task ClassifyAsync_ListOperationType_ShouldReturnSafe()
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.List
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.Safe);
        result.Action.Should().Be(SecurityAction.AutoApprove);
    }

    [Fact]
    public async Task ClassifyAsync_DeleteOperationType_ShouldReturnLowRisk()
    {
        var request = new ClassificationRequest
        {
            ToolName = "custom_tool",
            Parameters = new Dictionary<string, JsonElement>(),
            OperationType = OperationType.Delete
        };

        var result = await _sut.ClassifyAsync(request).ConfigureAwait(true);

        result.Classification.Should().Be(SecurityClassification.LowRisk);
        result.Action.Should().Be(SecurityAction.AutoApprove);
    }
}
