namespace Core.Tests;

/// <summary>
/// WriteDefense 链路编排测试 — 验证步骤顺序执行、任一短路、上下文在步骤间传递。
/// </summary>
public class WriteDefenseTests {
    [Fact]
    public async Task ExecuteAsync_AllStepsPass_ReturnsNullRejectionAndExecutesAll() {
        var executionOrder = new List<int>();
        var defense = WriteDefense
            .Begin("/test.txt", "content", FileOperationType.Write, "writing")
            .Then((_, _) => { executionOrder.Add(1); return ValueTask.FromResult<ToolResult?>(null); })
            .Then((_, _) => { executionOrder.Add(2); return ValueTask.FromResult<ToolResult?>(null); })
            .Then((_, _) => { executionOrder.Add(3); return ValueTask.FromResult<ToolResult?>(null); });

        var (context, rejection) = await defense.ExecuteAsync(CancellationToken.None);

        Assert.Null(rejection);
        Assert.Equal([1, 2, 3], executionOrder);
    }

    [Fact]
    public async Task ExecuteAsync_ShortCircuitsOnFirstRejection_SkipsRemaining() {
        var executionOrder = new List<int>();
        var rejectionResult = ToolResultBuilder.Error().WithText("rejected at 2").Build();
        var defense = WriteDefense
            .Begin("/test.txt", "content", FileOperationType.Write, "writing")
            .Then((_, _) => { executionOrder.Add(1); return ValueTask.FromResult<ToolResult?>(null); })
            .Then((_, _) => { executionOrder.Add(2); return ValueTask.FromResult<ToolResult?>(rejectionResult); })
            .Then((_, _) => { executionOrder.Add(3); return ValueTask.FromResult<ToolResult?>(null); });

        var (context, rejection) = await defense.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(rejection);
        Assert.Equal([1, 2], executionOrder);
    }

    [Fact]
    public async Task ExecuteAsync_FirstStepRejects_SkipsAllRemaining() {
        var executionOrder = new List<int>();
        var rejectionResult = ToolResultBuilder.Error().WithText("rejected at 1").Build();
        var defense = WriteDefense
            .Begin("/test.txt", "content", FileOperationType.Write, "writing")
            .Then((_, _) => { executionOrder.Add(1); return ValueTask.FromResult<ToolResult?>(rejectionResult); })
            .Then((_, _) => { executionOrder.Add(2); return ValueTask.FromResult<ToolResult?>(null); })
            .Then((_, _) => { executionOrder.Add(3); return ValueTask.FromResult<ToolResult?>(null); });

        var (_, rejection) = await defense.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(rejection);
        Assert.Equal([1], executionOrder);
    }

    /// <summary>
    /// 上下文在步骤间传递：步骤1修改 ResolvedPath，步骤2观察到修改后的值。
    /// 验证 ResolveSandboxAsync → 后续守卫用 ResolvedPath 的链路正确性。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ContextPropagatedBetweenSteps() {
        var observedPath = string.Empty;
        var defense = WriteDefense
            .Begin("/original.txt", "content", FileOperationType.Write, "writing")
            .Then((ctx, _) => { ctx.ResolvedPath = "/resolved.txt"; return ValueTask.FromResult<ToolResult?>(null); })
            .Then((ctx, _) => { observedPath = ctx.ResolvedPath; return ValueTask.FromResult<ToolResult?>(null); });

        await defense.ExecuteAsync(CancellationToken.None);

        Assert.Equal("/resolved.txt", observedPath);
    }

    /// <summary>
    /// Begin 正确初始化上下文：ResolvedPath 初始等于 OriginalPath。
    /// </summary>
    [Fact]
    public async Task Begin_InitializesContextCorrectly() {
        var defense = WriteDefense
            .Begin("/test.txt", "content", FileOperationType.Write, "writing", "old", "new", true);

        var (context, rejection) = await defense.ExecuteAsync(CancellationToken.None);

        Assert.Equal("/test.txt", context.OriginalPath);
        Assert.Equal("/test.txt", context.ResolvedPath);
        Assert.Equal("content", context.ContentToCheck);
        Assert.Equal(FileOperationType.Write, context.Operation);
        Assert.Equal("writing", context.OperationLabel);
        Assert.Equal("old", context.OldString);
        Assert.Equal("new", context.NewString);
        Assert.True(context.ReplaceAll);
        Assert.Null(rejection);
    }

    /// <summary>
    /// 链路结束后 ResolvedPath 用于实际写入 — 验证 FileWrite 链路用沙箱解析后路径写入。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReturnsContextWithResolvedPathForWrite() {
        var defense = WriteDefense
            .Begin("/original.txt", "content", FileOperationType.Write, "writing")
            .Then((ctx, _) => { ctx.ResolvedPath = "/sandbox/resolved.txt"; return ValueTask.FromResult<ToolResult?>(null); });

        var (context, _) = await defense.ExecuteAsync(CancellationToken.None);

        Assert.Equal("/sandbox/resolved.txt", context.ResolvedPath);
    }
}
