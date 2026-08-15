namespace MockServer.E2E.Tests;

/// <summary>
/// 模态不匹配拦截 E2E 覆盖测试 — 验证完整链路：
/// 媒介意图检测 → 注入系统提示 → AskUserQuestion → Agent 子代理 → 结果返回
/// </summary>
public sealed class ModalityMismatchCoverageTests : CoverageTestBase
{
    public ModalityMismatchCoverageTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ImageGenerationMismatch_ShouldCallAskUserQuestion()
    {
        await RunScriptAsync(ModalityMismatchScripts.ImageGenerationMismatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task VideoRecognitionMismatch_ShouldCallAskUserQuestion()
    {
        await RunScriptAsync(ModalityMismatchScripts.VideoRecognitionMismatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task ModalityMismatchWithAgentSpawn_ShouldCallAgentTool()
    {
        await RunScriptAsync(ModalityMismatchScripts.ModalityMismatchWithAgentSpawn).ConfigureAwait(true);
    }

    [Fact]
    public async Task NoMismatchForTextOnly_ShouldNotInjectPrompt()
    {
        await RunScriptAsync(ModalityMismatchScripts.NoMismatchForTextOnly).ConfigureAwait(true);
    }
}
