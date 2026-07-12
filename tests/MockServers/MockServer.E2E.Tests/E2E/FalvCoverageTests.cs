namespace MockServer.E2E.Tests;

/// <summary>
/// /falv 结构化推理 E2E 覆盖测试 — 假定/裁决/预算/续费/证据链
/// </summary>
public sealed class FalvCoverageTests : CoverageTestBase
{
    public FalvCoverageTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task FalvStatus_ShouldShowEmptyEngine()
    {
        await RunScriptAsync(FalvConversationScripts.FalvStatusEmpty).ConfigureAwait(true);
    }

    [Fact]
    public async Task FalvAddAssumption_ShouldAddAndShowInStatus()
    {
        await RunScriptAsync(FalvConversationScripts.FalvAddAssumption).ConfigureAwait(true);
    }

    [Fact]
    public async Task FalvJudge_ShouldRunAdversarialAndShowBudget()
    {
        await RunScriptAsync(FalvConversationScripts.FalvJudgeWithBudget).ConfigureAwait(true);
    }

    [Fact]
    public async Task FalvBudgetExhaust_ShouldPromptRefillAndContinue()
    {
        await RunScriptAsync(FalvConversationScripts.FalvBudgetExhaustAndRefill).ConfigureAwait(true);
    }

    [Fact]
    public async Task FalvEvidence_ShouldShowEmptyChain()
    {
        await RunScriptAsync(FalvConversationScripts.FalvEvidenceEmpty).ConfigureAwait(true);
    }

    [Fact]
    public async Task FalvHelp_ShouldShowAllSubcommands()
    {
        await RunScriptAsync(FalvConversationScripts.FalvHelp).ConfigureAwait(true);
    }
}
