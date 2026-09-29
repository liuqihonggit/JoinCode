
namespace Core.Goal.Tests;

public sealed class DecomposabilityAnalyzerTests {
    [Fact]
    public void ParseAnalysisResult_Json_Decomposable_With_SubTasks_Should_Return_Decomposable() {
        var content = """{"isDecomposable": true, "reason": "多个独立模块", "subTasks": [{"id": "sub_1", "title": "模块A", "description": "实现A", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal("多个独立模块", result.Reason);
        Assert.Single(result.SubTasks);
        Assert.Equal("sub_1", result.SubTasks[0].Id);
    }

    [Fact]
    public void ParseAnalysisResult_Json_NotDecomposable_Should_Return_NotDecomposable() {
        var content = """{"isDecomposable": false, "reason": "单文件修改", "subTasks": []}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.False(result.IsDecomposable);
        Assert.Equal("单文件修改", result.Reason);
        Assert.Empty(result.SubTasks);
    }

    [Fact]
    public void ParseAnalysisResult_Null_Should_Return_NotDecomposable() {
        var result = DecomposabilityAnalyzer.ParseAnalysisResult(null);

        Assert.False(result.IsDecomposable);
        Assert.Equal("分解分析器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseAnalysisResult_Empty_Should_Return_NotDecomposable() {
        var result = DecomposabilityAnalyzer.ParseAnalysisResult("");

        Assert.False(result.IsDecomposable);
        Assert.Equal("分解分析器返回空结果", result.Reason);
    }

    [Fact]
    public void ParseAnalysisResult_InvalidFormat_Should_Return_FormatError() {
        var result = DecomposabilityAnalyzer.ParseAnalysisResult("yes it can be split");

        Assert.False(result.IsDecomposable);
        Assert.Contains("格式异常", result.Reason);
    }

    [Fact]
    public void ParseAnalysisResult_Json_MissingId_Should_GenerateId() {
        var content = """{"isDecomposable": true, "reason": "ok", "subTasks": [{"id": "", "title": "T", "description": "D", "dependsOn": [], "ownedFiles": [], "priority": "medium", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Single(result.SubTasks);
        Assert.StartsWith("sub_", result.SubTasks[0].Id);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_Trailing_Comma_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "subTasks": [],}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
    }

    [Fact]
    public void ParseAnalysisResult_Json_In_CodeBlock_Should_Parse() {
        var content = "```json\n{\"isDecomposable\": true, \"reason\": \"多模块\", \"subTasks\": []}\n```";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal("多模块", result.Reason);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_Dependencies_Should_Preserve() {
        var content = """{"isDecomposable": true, "reason": "有依赖", "subTasks": [{"id": "sub_1", "title": "A", "description": "DA", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}, {"id": "sub_2", "title": "B", "description": "DB", "dependsOn": ["sub_1"], "ownedFiles": ["b.cs"], "priority": "medium", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(2, result.SubTasks.Count);
        Assert.Empty(result.SubTasks[0].DependsOn);
        Assert.Equal(["sub_1"], result.SubTasks[1].DependsOn);
    }

    [Fact]
    public async Task AnalyzeAsync_NullObjective_Should_Throw() {
        var kernel = new Mock<IChatClient>();
        var analyzer = new DecomposabilityAnalyzer(kernel.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            analyzer.AnalyzeAsync(null!, [])).ConfigureAwait(true);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyObjective_Should_Throw() {
        var kernel = new Mock<IChatClient>();
        var analyzer = new DecomposabilityAnalyzer(kernel.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            analyzer.AnalyzeAsync("", [])).ConfigureAwait(true);
    }

    [Fact]
    public async Task AnalyzeAsync_ChatServiceThrows_Should_Return_NotDecomposable() {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("网络错误"));

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        await using var analyzer = new DecomposabilityAnalyzer(kernel.Object);
        var result = await analyzer.AnalyzeAsync("实现功能", []).ConfigureAwait(true);

        Assert.False(result.IsDecomposable);
        Assert.Equal("分解分析器不可用", result.Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_ChatServiceReturnsValidJson_Should_Parse() {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"isDecomposable": true, "reason": "多模块可并行", "subTasks": [{"id": "sub_1", "title": "A", "description": "实现A", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        await using var analyzer = new DecomposabilityAnalyzer(kernel.Object);
        var result = await analyzer.AnalyzeAsync("重构多个模块", ["不修改API"]).ConfigureAwait(true);

        Assert.True(result.IsDecomposable);
        Assert.Single(result.SubTasks);
    }

    [Fact]
    public async Task AnalyzeAsync_ChatServiceReturnsEmpty_Should_Return_NotDecomposable() {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        await using var analyzer = new DecomposabilityAnalyzer(kernel.Object);
        var result = await analyzer.AnalyzeAsync("实现功能", []).ConfigureAwait(true);

        Assert.False(result.IsDecomposable);
        Assert.Equal("分解分析器返回空结果", result.Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_Pass_Constraints_To_Prompt() {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();

        MessageList? capturedHistory = null;
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"isDecomposable": false, "reason": "no", "subTasks": []}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        await using var analyzer = new DecomposabilityAnalyzer(kernel.Object);
        await analyzer.AnalyzeAsync("实现功能", ["不修改公共API"]).ConfigureAwait(true);

        Assert.NotNull(capturedHistory);
        Assert.True(capturedHistory.Count >= 2);

        var systemMessage = capturedHistory[0];
        Assert.Equal(MessageRole.System, systemMessage.Role);
        Assert.Contains("不修改公共API", systemMessage.Content);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_CaseInsensitive_Should_Parse() {
        var content = """{"IsDecomposable": true, "Reason": "ok", "SubTasks": []}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_Comment_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "subTasks": [] /* parallel */}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_ComplexityLow_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "complexity": "low", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ComplexityLevel.Low, result.Complexity);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_ComplexityHigh_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "complexity": "high", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ComplexityLevel.High, result.Complexity);
    }

    [Fact]
    public void ParseAnalysisResult_Json_MissingComplexity_Should_DefaultToMedium() {
        var content = """{"isDecomposable": true, "reason": "ok", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ComplexityLevel.Medium, result.Complexity);
    }

    [Fact]
    public void ParseAnalysisResult_Json_InvalidComplexity_Should_DefaultToMedium() {
        var content = """{"isDecomposable": true, "reason": "ok", "complexity": "ultra", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ComplexityLevel.Medium, result.Complexity);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_ModeB_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "complexity": "low", "mode": "B", "rationale": "independent tasks", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ExecutionMode.PlanB, result.Mode);
        Assert.Equal("independent tasks", result.Rationale);
    }

    [Fact]
    public void ParseAnalysisResult_Json_With_ModeA_Should_Parse() {
        var content = """{"isDecomposable": true, "reason": "ok", "complexity": "medium", "mode": "A", "rationale": "sequential pipeline", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ExecutionMode.PlanA, result.Mode);
        Assert.Equal("sequential pipeline", result.Rationale);
    }

    [Fact]
    public void ParseAnalysisResult_Json_MissingMode_Should_DefaultToPlanA() {
        var content = """{"isDecomposable": true, "reason": "ok", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ExecutionMode.PlanA, result.Mode);
    }

    [Fact]
    public void ParseAnalysisResult_Json_InvalidMode_Should_DefaultToPlanA() {
        var content = """{"isDecomposable": true, "reason": "ok", "mode": "C", "subTasks": [{"id": "sub_1", "title": "A", "description": "D", "dependsOn": [], "ownedFiles": ["a.cs"], "priority": "high", "variant": "code"}]}""";

        var result = DecomposabilityAnalyzer.ParseAnalysisResult(content);

        Assert.True(result.IsDecomposable);
        Assert.Equal(ExecutionMode.PlanA, result.Mode);
    }

    [Fact]
    public async Task AnalyzeAsync_Prompt_Should_Contain_ThinkingChain() {
        var kernel = new Mock<IChatClient>();
        var chatService = new Mock<IQueryService>();

        MessageList? capturedHistory = null;
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = """{"isDecomposable": false, "reason": "no", "subTasks": []}""" }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        await using var analyzer = new DecomposabilityAnalyzer(kernel.Object);
        await analyzer.AnalyzeAsync("实现功能", []).ConfigureAwait(true);

        Assert.NotNull(capturedHistory);
        var systemMessage = capturedHistory[0].Content;
        Assert.Contains("THINKING CHAIN", systemMessage);
        Assert.Contains("Step 1", systemMessage);
        Assert.Contains("Step 2", systemMessage);
        Assert.Contains("Step 3", systemMessage);
        Assert.Contains("Step 4", systemMessage);
        Assert.Contains("EXECUTION MODES", systemMessage);
        Assert.Contains("\"mode\"", systemMessage);
        Assert.Contains("\"rationale\"", systemMessage);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Objective() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("重构订单模块", []);

        Assert.Contains("重构订单模块", prompt);
        Assert.Contains("OBJECTIVE:", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Constraints() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("实现功能", ["不修改公共API", "覆盖率>80%"]);

        Assert.Contains("不修改公共API", prompt);
        Assert.Contains("覆盖率>80%", prompt);
        Assert.Contains("CONSTRAINTS:", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_EmptyConstraints_Should_Contain_Default_Marker() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("实现功能", []);

        Assert.Contains("无特殊约束", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_ThinkingChain_Steps() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", []);

        Assert.Contains("THINKING CHAIN", prompt);
        Assert.Contains("Step 1", prompt);
        Assert.Contains("Step 2", prompt);
        Assert.Contains("Step 3", prompt);
        Assert.Contains("Step 4", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Decomposability_Rules() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", []);

        Assert.Contains("DECOMPOSABILITY RULES", prompt);
        Assert.Contains("isDecomposable", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Complexity_Levels() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", []);

        Assert.Contains("COMPLEXITY LEVELS", prompt);
        Assert.Contains("\"low\"", prompt);
        Assert.Contains("\"medium\"", prompt);
        Assert.Contains("\"high\"", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Execution_Modes() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", []);

        Assert.Contains("EXECUTION MODES", prompt);
        Assert.Contains("\"A\"", prompt);
        Assert.Contains("\"B\"", prompt);
        Assert.Contains("serial-first", prompt);
        Assert.Contains("parallel-first", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_Should_Contain_Response_Format() {
        var prompt = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", []);

        Assert.Contains("RESPONSE FORMAT", prompt);
        Assert.Contains("```json", prompt);
        Assert.Contains("subTasks", prompt);
        Assert.Contains("ownedFiles", prompt);
        Assert.Contains("dependsOn", prompt);
    }

    [Fact]
    public void BuildAnalyzerPrompt_DifferentObjectives_Should_Produce_Different_Prompts() {
        var prompt1 = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标A", []);
        var prompt2 = DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标B", []);

        Assert.NotEqual(prompt1, prompt2);
    }

    [Fact]
    public void BuildAnalyzerConstraintsText_Empty_Should_Return_Default_Marker() {
        var text = DecomposabilityAnalyzer.BuildAnalyzerConstraintsText([]);

        Assert.Equal("无特殊约束", text);
    }

    [Fact]
    public void BuildAnalyzerConstraintsText_Single_Should_Prefix_With_Dash() {
        var text = DecomposabilityAnalyzer.BuildAnalyzerConstraintsText(["只读模式"]);

        Assert.Equal("- 只读模式", text);
    }

    [Fact]
    public void BuildAnalyzerConstraintsText_Multiple_Should_Join_With_Newline() {
        var text = DecomposabilityAnalyzer.BuildAnalyzerConstraintsText(["A", "B"]);

        Assert.Equal("- A\n- B", text);
    }

    [Fact]
    public void BuildAnalyzerConstraintsText_Null_Should_Throw_ArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() =>
            DecomposabilityAnalyzer.BuildAnalyzerConstraintsText(null!));
    }

    [Fact]
    public void BuildAnalyzerPrompt_NullObjective_Should_Throw_ArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() =>
            DecomposabilityAnalyzer.BuildAnalyzerPrompt(null!, []));
    }

    [Fact]
    public void BuildAnalyzerPrompt_EmptyObjective_Should_Throw_ArgumentException() {
        Assert.Throws<ArgumentException>(() =>
            DecomposabilityAnalyzer.BuildAnalyzerPrompt("", []));
    }

    [Fact]
    public void BuildAnalyzerPrompt_WhiteSpaceObjective_Should_Throw_ArgumentException() {
        Assert.Throws<ArgumentException>(() =>
            DecomposabilityAnalyzer.BuildAnalyzerPrompt("   ", []));
    }

    [Fact]
    public void BuildAnalyzerPrompt_NullConstraints_Should_Throw_ArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() =>
            DecomposabilityAnalyzer.BuildAnalyzerPrompt("目标", null!));
    }
}