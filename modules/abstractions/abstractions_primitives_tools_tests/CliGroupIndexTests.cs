namespace CliNodes.Tests;

/// <summary>
/// 首轮提示词分组索引契约 —— 锁定「提示词里列出的每个 exe 都真实存在且能一句话定位」。
/// <para>存在理由：提示词原来写的是「540+ 工具，用 bash 逐层发现」，模型不知道有哪些 exe 可查，
/// 只能猜命令名；猜不中就退回裸 shell 硬拼。索引把「有哪些 exe」变成数据，模型再用 <c>-h</c> 按需读。</para>
/// </summary>
public sealed class CliGroupIndexTests {
    [Fact]
    public void LoadDefault_FromEmbeddedResource_CoversEveryGroupingShipment() {
        var index = CliGroupIndex.LoadDefault();

        // 13 组是当前装配口径；这里只锁「至少覆盖全部功能大类」，不锁具体数字，
        // 免得加组时被迫改测试（真正的数字对账在 Verify-ToolNodes.ps1 里与 node.yaml 比）。
        Assert.True(index.GroupCount >= 10, $"分组索引只有 {index.GroupCount} 组，疑似生成器没跑全");
        var ids = index.Groups.Select(group => group.Id).ToArray();
        Assert.Contains("file", ids);
        Assert.Contains("git", ids);
        Assert.Contains("search", ids);
        Assert.Contains("slash", ids);
    }

    [Fact]
    public void LoadDefault_EveryEntryCarriesExeNameAndHumanBlurb() {
        var index = CliGroupIndex.LoadDefault();

        Assert.All(index.Groups, group => {
            Assert.False(string.IsNullOrWhiteSpace(group.Blurb), $"分组 '{group.Id}' 缺一句话定位，提示词里会变成无名 exe");
            // exe 名必须能直接当命令敲：带 .exe 后缀在 bash 里也能跑，但 `-h` 例子会更啰嗦，统一不带。
            Assert.DoesNotContain(".exe", group.Exe);
            Assert.StartsWith("jcc", group.Exe, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LoadDefault_TotalsAgreeWithPerGroupBreakdown() {
        var index = CliGroupIndex.LoadDefault();

        Assert.Equal(index.Groups.Sum(group => group.ToolCount), index.ToolCount);
        Assert.Equal(index.Groups.Sum(group => group.LocalCount + group.DelegateCount), index.IsolatedToolCount);
        Assert.All(index.Groups, group => {
            Assert.True(group.ToolCount > 0, $"分组 '{group.Id}' 工具数为 0");
            Assert.True(group.IsolatedCount <= group.ToolCount,
                $"分组 '{group.Id}' 的隔离数 {group.IsolatedCount} 超过工具总数 {group.ToolCount}");
        });
    }

    [Fact]
    public void LoadDefault_EveryGroupCarriesToolIdsMatchingCount() {
        var index = CliGroupIndex.LoadDefault();

        Assert.All(index.Groups, group => {
            Assert.Equal(group.ToolCount, group.ToolIds.Count);
            Assert.Equal(group.ToolIds.Count, group.ToolIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    [Fact]
    public void LoadDefault_IsolatedShareIsAtLeastHalfOfAllTools() {
        var index = CliGroupIndex.LoadDefault();

        // 不是审美问题：低于一半说明「独立 exe」退化成少数派，宿主又变回主执行者，
        // 那 ADR 0157 的进程隔离目标事实上没达成，必须在 CI 就炸出来。
        Assert.True(index.IsolatedToolCount * 2 >= index.ToolCount,
            $"仅 {index.IsolatedToolCount}/{index.ToolCount} 工具进程隔离");
    }

    [Fact]
    public void FromText_EntryMissingBlurb_IsRejected() {
        const string yaml = """
            version: 1
            groups:
              - id: probe
                exe: jcc-tool-probe
                tools: 3
                local: 3
                delegate: 0
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => CliGroupIndex.FromText(yaml, "probe.yaml"));
        Assert.Contains("缺少 blurb", ex.Message);
    }

    [Fact]
    public void FromText_DuplicateGroupId_IsRejected() {
        const string yaml = """
            version: 1
            groups:
              - id: file
                exe: jcc-tool-file
                blurb: 文件
                tools: 1
                local: 1
                delegate: 0
              - id: file
                exe: jcc-tool-file2
                blurb: 又是文件
                tools: 1
                local: 0
                delegate: 1
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => CliGroupIndex.FromText(yaml, "dup.yaml"));
        Assert.Contains("重复分组 id 'file'", ex.Message);
    }
}
