namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 指针/游标前进守卫测试 — #105 三阶变异挂死免疫
/// <para>背景(Mutation #105 CodeIndex-Parsing/Rest 双片超时): 赋值语句删除与一元符号变异
/// (`current = current.Parent` 删除、`step = prev` 删除、`lc--`→`lc++`、`_layers.Add` 删除)
/// 造成指针永不前进的空转死循环 → testhost 挂死 → Stryker "Retrying the test session" 空转 35min。
/// 防御统一为 SpinWait.Count 守卫(框架内部计数器,单点变异无法破坏,与 CasRetry 同款已验证策略)。</para>
/// <para>本文件验证守卫不误伤: 正常路径迭代次数远小于守卫上限,大输入场景必须正常完成。</para>
/// </summary>
public sealed class PointerAdvanceGuardTests {

    #region GraphAnalytics.RebuildPath — 回溯指针前进

    private static CallEdge MakeEdge(string from, string to)
        => new() {
            CallerSymbol = from,
            CalleeSymbol = to,
            CallSiteFilePath = "test.cs",
            CallSiteLine = 1,
            CallKind = CallKind.Direct,
        };

    [Fact]
    public void RebuildPath_ShortChain_ReturnsFullPath() {
        // A→B→C, 回溯 C→A 共 2 步
        var predecessor = new Dictionary<string, (string, CallEdge)> {
            ["B"] = ("A", MakeEdge("A", "B")),
            ["C"] = ("B", MakeEdge("B", "C")),
        };

        var (nodes, edges) = GraphAnalytics.RebuildPath(predecessor, "A", "C");

        nodes.Should().Equal("A", "B", "C");
        edges.Should().HaveCount(2);
    }

    [Fact]
    public void RebuildPath_DirectEdge_SingleStep() {
        var predecessor = new Dictionary<string, (string, CallEdge)> {
            ["B"] = ("A", MakeEdge("A", "B")),
        };

        var (nodes, _) = GraphAnalytics.RebuildPath(predecessor, "A", "B");

        nodes.Should().Equal("A", "B");
    }

    [Fact]
    public void RebuildPath_LongChain_CompletesWithinGuardLimit() {
        // 1000 级链: 回溯步数 = predecessor.Count - 1, 永不超过守卫上界 predecessor.Count, 不误伤
        const int chainLength = 1000;
        var predecessor = new Dictionary<string, (string, CallEdge)>(chainLength);
        for (var i = 1; i <= chainLength; i++)
            predecessor[$"S{i}"] = ($"S{i - 1}", MakeEdge($"S{i - 1}", $"S{i}"));

        var (nodes, edges) = GraphAnalytics.RebuildPath(predecessor, "S0", $"S{chainLength}");

        nodes.Should().HaveCount(chainLength + 1);
        nodes[0].Should().Be("S0");
        nodes[^1].Should().Be($"S{chainLength}");
        edges.Should().HaveCount(chainLength);
    }

    #endregion

    #region HnswAnn — 层级初始化与逐层下降游标

    [Fact]
    public void HnswAnn_BulkAdd_LayerGuardsDoNotMisfire() {
        // 200 个随机向量批量插入: HNSW 随机层级(上限~log 级别)触发多层建图,
        // 层级初始化/逐层下降/逐层插入三个守卫(上限128)均不得误触发
        var ann = new HnswAnn(m: 8, efConstruction: 32, efSearch: 16);
        var rng = new Random(42);
        var items = new List<(string Id, float[] Vector)>();
        for (var i = 0; i < 200; i++)
            items.Add(($"v{i}", [rng.NextSingle(), rng.NextSingle(), rng.NextSingle()]));

        ann.AddRange(items);

        ann.Count.Should().Be(200);
        var results = ann.Search([0.5f, 0.5f, 0.5f], 5, CancellationToken.None);
        results.Should().HaveCount(5);
    }

    [Fact]
    public void HnswAnn_RepeatedAdd_GuardsDoNotAccumulate() {
        // 逐条插入 100 次: 守卫 SpinWait 为局部变量,每次 Add 重置,不跨调用累积误伤
        var ann = new HnswAnn(m: 4, efConstruction: 16, efSearch: 8);
        for (var i = 0; i < 100; i++)
            ann.Add($"v{i}", [i * 0.01f, 1f - i * 0.01f]);

        ann.Count.Should().Be(100);
    }

    #endregion
}
