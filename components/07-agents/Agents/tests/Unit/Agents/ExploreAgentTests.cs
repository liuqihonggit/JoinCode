
namespace Core.Tests.Agents;

public class ExploreAgentTests
{
    private readonly IChatClient _kernel;
    private readonly IFileSystem _fs;
    private readonly Mock<IReferenceResolver> _referenceResolverMock;
    private readonly ExploreAgent _agent;
    private readonly ExploreAgent _agentWithResolver;

    public ExploreAgentTests()
    {
        _kernel = ServiceRegistration.CreateEmptyKernel();
        _fs = new IO.FileSystem.PhysicalFileSystem();

        _referenceResolverMock = new Mock<IReferenceResolver>();

        _agent = new ExploreAgent(
            _kernel,
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            _fs);

        _agentWithResolver = new ExploreAgent(
            _kernel,
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            _fs,
            _referenceResolverMock.Object);
    }

    #region 基础属性测试

    [Fact]
    public void Constructor_InitializesProperties()
    {
        Assert.Equal("ExploreAgent", _agent.Name);
        Assert.Equal(BuiltInAgentType.Explore, _agent.AgentType);
        Assert.Contains("探索", _agent.Description);
        Assert.NotNull(_agent.SystemPrompt);
    }

    [Fact]
    public void Constructor_WithReferenceResolver_InitializesResolver()
    {
        var agent = new ExploreAgent(_kernel, JoinCode.Abstractions.Clock.SystemClockService.Instance, _fs, _referenceResolverMock.Object);

        Assert.NotNull(agent);
        Assert.Equal("ExploreAgent", agent.Name);
    }

    [Fact]
    public async Task GetContext_ReturnsValidContext()
    {
        var context = await _agent.GetContextAsync().ConfigureAwait(true);

        Assert.NotNull(context);
        Assert.NotEmpty(context.Messages);
        Assert.Equal("system", context.Messages[0].Role);
        Assert.Equal(_agent.SystemPrompt, context.Messages[0].Content);
    }

    [Fact]
    public async Task ClearContext_ResetsToInitialState()
    {
        var originalContext = await _agent.GetContextAsync().ConfigureAwait(true);

        await _agent.ClearContextAsync().ConfigureAwait(true);
        var newContext = await _agent.GetContextAsync().ConfigureAwait(true);

        Assert.Equal(originalContext.Messages.Count, newContext.Messages.Count);
        Assert.Equal(originalContext.Messages[0].Content, newContext.Messages[0].Content);
    }

    #endregion

    #region ExploreRequest 测试

    [Fact]
    public void ExploreRequest_CanBeCreated()
    {
        var request = new ExploreRequest
        {
            TargetPath = "/test/path",
            FocusArea = "核心组件",
            Questions = new List<string> { "问题1", "问题2" },
            Depth = ExploreDepth.Detailed
        };

        Assert.Equal("/test/path", request.TargetPath);
        Assert.Equal("核心组件", request.FocusArea);
        Assert.Equal(2, request.Questions.Count);
        Assert.Equal(ExploreDepth.Detailed, request.Depth);
    }

    [Fact]
    public void ExploreRequest_DefaultValues()
    {
        var request = new ExploreRequest
        {
            TargetPath = "/test/path"
        };

        Assert.Equal("/test/path", request.TargetPath);
        Assert.Null(request.FocusArea);
        Assert.Null(request.Questions);
        Assert.Null(request.Depth);
    }

    #endregion

    #region ExploreReferenceRequest 测试

    [Fact]
    public void ExploreReferenceRequest_CanBeCreated()
    {
        var request = new ExploreReferenceRequest
        {
            ReferencePath = "claude-code/src/tools",
            SearchDepth = 5,
            IncludeSubdirectories = true,
            FocusArea = "工具实现",
            Questions = new List<string> { "如何使用?", "最佳实践?" },
            MaxFiles = 50,
            IncludeFileContent = true
        };

        Assert.Equal("claude-code/src/tools", request.ReferencePath);
        Assert.Equal(5, request.SearchDepth);
        Assert.True(request.IncludeSubdirectories);
        Assert.Equal("工具实现", request.FocusArea);
        Assert.Equal(2, request.Questions.Count);
        Assert.Equal(50, request.MaxFiles);
        Assert.True(request.IncludeFileContent);
    }

    [Fact]
    public void ExploreReferenceRequest_DefaultValues()
    {
        var request = new ExploreReferenceRequest
        {
            ReferencePath = "test/path"
        };

        Assert.Equal("test/path", request.ReferencePath);
        Assert.Equal(3, request.SearchDepth);
        Assert.True(request.IncludeSubdirectories);
        Assert.Null(request.FocusArea);
        Assert.Null(request.Questions);
        Assert.Equal(20, request.MaxFiles);
        Assert.True(request.IncludeFileContent);
    }

    [Fact]
    public void ExploreReferenceRequest_Create_ReturnsDefaultRequest()
    {
        var request = ExploreReferenceRequest.Create("tools");

        Assert.Equal("tools", request.ReferencePath);
        Assert.Equal(3, request.SearchDepth);
        Assert.True(request.IncludeSubdirectories);
    }

    [Fact]
    public void ExploreReferenceRequest_CreateWithFocus_ReturnsRequestWithFocus()
    {
        var request = ExploreReferenceRequest.CreateWithFocus("tools", "实现细节");

        Assert.Equal("tools", request.ReferencePath);
        Assert.Equal("实现细节", request.FocusArea);
        Assert.Equal(3, request.SearchDepth);
    }

    #endregion

    #region ExploreReferenceResult 测试

    [Fact]
    public void ExploreReferenceResult_SuccessResult_CanBeCreated()
    {
        var resolvedReference = CreateTestCodeReference();
        var files = new List<ExploredFile>
        {
            CreateTestExploredFile("test.cs", "/path/test.cs")
        };
        var tokenUsage = new TokenUsage { PromptTokens = 100, CompletionTokens = 50 };

        var result = ExploreReferenceResult.SuccessResult(
            resolvedReference,
            files,
            "测试摘要",
            "test123",
            1000,
            tokenUsage);

        Assert.True(result.Success);
        Assert.NotNull(result.ResolvedReference);
        Assert.Single(result.Files);
        Assert.Equal("测试摘要", result.Summary);
        Assert.Equal("test123", result.ExploreId);
        Assert.Equal(1000, result.ExecutionTimeMs);
        Assert.Equal(100, result.TokenUsage.PromptTokens);
        Assert.True(result.IsReferenceResolved);
    }

    [Fact]
    public void ExploreReferenceResult_FailureResult_CanBeCreated()
    {
        var result = ExploreReferenceResult.FailureResult("解析失败");

        Assert.False(result.Success);
        Assert.Equal("解析失败", result.ErrorMessage);
        Assert.Null(result.ResolvedReference);
        Assert.Empty(result.Files);
        Assert.False(result.IsReferenceResolved);
    }

    [Fact]
    public void ExploredFile_CanBeCreated()
    {
        var file = new ExploredFile
        {
            FilePath = "/project/src/test.cs",
            RelativePath = "src/test.cs",
            FileType = "cs",
            FileSize = 1024,
            LastModified = DateTime.UtcNow,
            Content = "public class Test {}",
            RelevanceScore = 0.95,
            MatchDescription = "精确匹配"
        };

        Assert.Equal("/project/src/test.cs", file.FilePath);
        Assert.Equal("src/test.cs", file.RelativePath);
        Assert.Equal("cs", file.FileType);
        Assert.Equal(1024, file.FileSize);
        Assert.Equal("public class Test {}", file.Content);
        Assert.Equal(0.95, file.RelevanceScore);
        Assert.Equal("精确匹配", file.MatchDescription);
    }

    [Fact]
    public void CodeSnippet_CanBeCreated()
    {
        var snippet = new CodeSnippet
        {
            SourceFile = "/project/src/test.cs",
            Content = "public void Test() { }",
            StartLine = 10,
            EndLine = 12,
            Language = "csharp",
            Description = "测试方法",
            RelevanceScore = 0.9
        };

        Assert.Equal("/project/src/test.cs", snippet.SourceFile);
        Assert.Equal("public void Test() { }", snippet.Content);
        Assert.Equal(10, snippet.StartLine);
        Assert.Equal(12, snippet.EndLine);
        Assert.Equal("csharp", snippet.Language);
        Assert.Equal("测试方法", snippet.Description);
        Assert.Equal(0.9, snippet.RelevanceScore);
    }

    #endregion

    #region ExploreResult 测试

    [Fact]
    public void ExploreResult_CanBeCreated()
    {
        var result = new ExploreResult
        {
            Success = true,
            ExploreId = "test123",
            Content = "探索结果内容",
            ExecutionTimeMs = 1000,
            TokenUsage = new TokenUsage { PromptTokens = 100, CompletionTokens = 50 }
        };

        Assert.True(result.Success);
        Assert.Equal("test123", result.ExploreId);
        Assert.Equal("探索结果内容", result.Content);
        Assert.Equal(1000, result.ExecutionTimeMs);
        Assert.Equal(100, result.TokenUsage.PromptTokens);
    }

    #endregion

    #region 引用解析功能测试

    [Fact]
    public async Task ExploreWithReferenceAsync_WithoutResolver_ReturnsFailure()
    {
        var request = ExploreReferenceRequest.Create("tools");

        var result = await _agent.ExploreWithReferenceAsync(request).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("未配置引用解析器", result.ErrorMessage);
    }

    [Fact]
    public async Task ExploreWithReferenceAsync_WithUnresolvedReference_ReturnsFailure()
    {
        var request = ExploreReferenceRequest.Create("nonexistent");
        var unresolvedRef = CodeReference.Unresolved("nonexistent");

        _referenceResolverMock
            .Setup(r => r.ResolveCodeReferenceAsync(
                "nonexistent",
                It.IsAny<ReferenceResolutionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unresolvedRef);

        var result = await _agentWithResolver.ExploreWithReferenceAsync(request).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("无法解析引用", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAndExploreAsync_WithoutResolver_ReturnsFailure()
    {
        var result = await _agent.ResolveAndExploreAsync("tools").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("未配置引用解析器", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAndExploreAsync_WithFocusArea_WithoutResolver_ReturnsFailure()
    {
        var result = await _agent.ResolveAndExploreAsync("tools", "实现").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("未配置引用解析器", result.ErrorMessage);
    }

    #endregion

    #region 辅助方法

    private static CodeReference CreateTestCodeReference()
    {
        return new CodeReference
        {
            ReferencePath = "tools",
            ResolvedPath = "/project/src/tools",
            MatchType = ReferenceMatchType.Exact,
            RelevanceScore = 1.0,
            FileMatches = new List<FileMatch>
            {
                FileMatch.Create("/project/src/tools/Tool1.cs", ReferenceMatchType.Exact, 1.0, "精确匹配"),
                FileMatch.Create("/project/src/tools/Tool2.cs", ReferenceMatchType.Exact, 1.0, "精确匹配")
            }
        };
    }

    private static ExploredFile CreateTestExploredFile(string fileName, string filePath)
    {
        return new ExploredFile
        {
            FilePath = filePath,
            RelativePath = fileName,
            FileType = "cs",
            FileSize = 1024,
            LastModified = DateTime.UtcNow,
            RelevanceScore = 0.95,
            MatchDescription = "测试文件"
        };
    }

    #endregion
}
