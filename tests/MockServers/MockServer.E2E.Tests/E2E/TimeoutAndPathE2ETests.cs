namespace MockServer.E2E.Tests;

/// <summary>
/// 脚本超时关键字 + 路径乱码检测 E2E 测试
/// 验证端到端链路: LLM输出 → 工具调用 → 权限检查/超时中间件 → 结果返回
/// </summary>
[Trait("Category", "Integration")]
public sealed class TimeoutAndPathE2ETests : CoverageTestBase
{
    public TimeoutAndPathE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task SleepKeyword_ShouldAutoExtendTimeout()
        => await RunScriptAsync(TimeoutAndPathScripts.SleepKeywordAutoExtendsTimeout).ConfigureAwait(true);

    [Fact]
    public async Task TimeoutKeywordConflict_ShouldReturnErrorToAi()
        => await RunScriptAsync(TimeoutAndPathScripts.TimeoutKeywordConflictReturnsError).ConfigureAwait(true);

    [Fact]
    public async Task GarbledPath_ShouldDirectError_WithoutAskPanel()
        => await RunScriptAsync(TimeoutAndPathScripts.GarbledPathDirectErrorNoAskPanel).ConfigureAwait(true);

    [Fact]
    public async Task NonExistentPath_ShouldDirectError_WithoutAskPanel()
        => await RunScriptAsync(TimeoutAndPathScripts.NonExistentPathDirectErrorNoAskPanel).ConfigureAwait(true);
}
