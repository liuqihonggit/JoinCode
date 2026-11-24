using JoinCode.Abstractions.Security.Permission;

namespace NativeTui.Host.Tests;

/// <summary>
/// 权限解释装配（TASK069-P4）— 契约层 <see cref="IPermissionExplainer"/> → Core 委托的适配：
/// 三档风险一一映射、解释器缺失时 enabled=false、调用抛异常一律折成 null。
/// <para>这一层最容易出的错是「映射归并」：把 Medium 归成 High 会让用户对普通可恢复操作过度紧张，
/// 归成 Low 又会让不可撤回操作显得无害。</para>
/// </summary>
public sealed class PermissionExplainerAssemblyTests {
    /// <summary>三档风险等级逐档映射，不做归并。</summary>
    [Theory]
    [InlineData(PermissionRiskLevel.Low)]
    [InlineData(PermissionRiskLevel.Medium)]
    [InlineData(PermissionRiskLevel.High)]
    public void MapRiskBand_PreservesEachBand(PermissionRiskLevel level) {
        var band = PermissionExplainerAssembly.MapRiskBand(level);

        Assert.Equal(level switch {
            PermissionRiskLevel.Low => PermissionRiskBand.Low,
            PermissionRiskLevel.Medium => PermissionRiskBand.Medium,
            _ => PermissionRiskBand.High,
        }, band);
    }

    /// <summary>解释器缺失（无 LLM 的宿主）→ 开关关闭，不塞一个会失败的假加载器。</summary>
    [Fact]
    public void Attach_WithoutExplainer_DisablesEntry() {
        var shell = CreateShell();
        var services = new ServiceCollection().BuildServiceProvider();

        PermissionExplainerAssembly.Attach(shell, services);

        Assert.False(shell.Permission.ExplanationEnabled);
        Assert.Null(shell.PermissionExplanationLoader);
    }

    /// <summary>解释器在但开关关闭 → loader 仍装配，enabled=false（重启开关无需重建 shell 的语义留给显式重绑）。</summary>
    [Fact]
    public void Attach_DisabledExplainer_KeepsEntryOff() {
        var shell = CreateShell();
        var services = new ServiceCollection()
            .AddSingleton<IPermissionExplainer>(new FakeExplainer(enabled: false, result: null))
            .BuildServiceProvider();

        PermissionExplainerAssembly.Attach(shell, services);

        Assert.False(shell.Permission.ExplanationEnabled);
    }

    /// <summary>正常路径 — 委托把契约结果映射成 Core 视图（含风险档位）。</summary>
    [Fact]
    public async Task Attach_EnabledExplainer_MapsResultToView() {
        var shell = CreateShell();
        var services = new ServiceCollection()
            .AddSingleton<IPermissionExplainer>(new FakeExplainer(enabled: true, new PermissionExplanation {
                RiskLevel = PermissionRiskLevel.High,
                Explanation = "删除目录",
                Reasoning = "我需要清理产物",
                Risk = "不可撤回",
            }))
            .BuildServiceProvider();

        PermissionExplainerAssembly.Attach(shell, services);
        var loader = shell.PermissionExplanationLoader;

        Assert.True(shell.Permission.ExplanationEnabled);
        Assert.NotNull(loader);
        var view = await loader("Bash", "需要确认: rm -rf build", CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal("删除目录", view.Explanation);
        Assert.Equal(PermissionRiskBand.High, view.RiskBand);
    }

    /// <summary>解释器抛异常 → 委托折成 null（不得冒泡到权限确认路径）。</summary>
    [Fact]
    public async Task Attach_ExplainerThrows_ReturnsNull() {
        var shell = CreateShell();
        var services = new ServiceCollection()
            .AddSingleton<IPermissionExplainer>(new ThrowingExplainer())
            .BuildServiceProvider();
        PermissionExplainerAssembly.Attach(shell, services);
        var loader = shell.PermissionExplanationLoader;

        Assert.NotNull(loader);
        Assert.Null(await loader("Bash", "x", CancellationToken.None));
    }

    private static ReplShell CreateShell() {
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(new IdleReplEngine(), banner, new StringWriter());
        shell.SeedContent();
        return shell;
    }

    /// <summary>空引擎（shell 构造需要，本测试不驱动回合）。</summary>
    private sealed class IdleReplEngine : IReplEngine {
        public string Submit(string text, IReadOnlyList<BridgeImageAttachment>? images = null, string? queryId = null) =>
            string.IsNullOrWhiteSpace(queryId) ? "q-test" : queryId;

        public void Interrupt(string? queryId = null) {
        }

        public void SetPermissionMode(string mode) {
        }

        public IReadOnlyList<BridgeEvent> DrainEvents() => [];

        public bool IsAlive => true;
    }

    private sealed class FakeExplainer(bool enabled, PermissionExplanation? result) : IPermissionExplainer {
        public bool IsEnabled => enabled;

        public Task<PermissionExplanation?> ExplainAsync(PermissionExplainRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingExplainer : IPermissionExplainer {
        public bool IsEnabled => true;

        public Task<PermissionExplanation?> ExplainAsync(PermissionExplainRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider down");
    }
}
