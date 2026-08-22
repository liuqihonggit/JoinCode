namespace MockServer.E2E.Tests;

/// <summary>
/// 模态不匹配拦截 E2E 覆盖测试 — 验证完整链路：
/// 媒介意图检测 → 注入标准报错文本 → ModelSearch 查找模型 → Agent 子代理 → 结果返回
/// </summary>
public sealed class ModalityMismatchCoverageTests : CoverageTestBase
{
    public ModalityMismatchCoverageTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ImageGenerationMismatch_ShouldCallModelSearch()
    {
        await RunScriptAsync(ModalityMismatchScripts.ImageGenerationMismatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task VideoRecognitionMismatch_ShouldCallModelSearch()
    {
        await RunScriptAsync(ModalityMismatchScripts.VideoRecognitionMismatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImageRecognitionMismatch_ShouldCallModelSearch()
    {
        await RunScriptAsync(ModalityMismatchScripts.ImageRecognitionMismatch).ConfigureAwait(true);
    }

    [Fact]
    public async Task ModalityMismatchWithAgentSpawn_ShouldCallAgentTool()
    {
        await RunScriptAsync(ModalityMismatchScripts.ModalityMismatchWithAgentSpawn).ConfigureAwait(true);
    }

    /// <summary>
    /// 识图完整链路 — 纯文本模型 + 识图任务 → ModelSearch(map[readImage]) → Agent(识图模型) → 看图片内容
    /// </summary>
    [Fact]
    public async Task ImageRecognitionWithAgentSpawn_ShouldCallAgentTool()
    {
        await RunScriptAsync(ModalityMismatchScripts.ImageRecognitionWithAgentSpawn).ConfigureAwait(true);
    }

    [Fact]
    public async Task NoMismatchForTextOnly_ShouldNotInjectPrompt()
    {
        await RunScriptAsync(ModalityMismatchScripts.NoMismatchForTextOnly).ConfigureAwait(true);
    }
}
