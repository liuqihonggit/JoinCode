namespace NativeTui.Tests.Snapshot;

/// <summary>
/// ReferenceScene 采集场景测试 — L2 截图校准的 C# 侧确定性数据源（80×24 固定场景）：
/// ① 前三行为 Clawd 像素画（与 compare_reference.py 的 clawd region 提取对齐）
/// ② 存在以全宽 ─ 线开头的输入框边框行（与 py 的 prompt region 提取对齐）
/// ③ 整帧字节确定（同一进程内两次调用 Ordinal 相等 — approved 快照的前提）
/// ④ 整帧 ANSI 落 approved 快照（Verify 模式：首跑生成 received 供审核）。
/// </summary>
public sealed class ReferenceSceneTests
{
    [Fact]
    public void ComposeAnsi_FirstThreeRowsAreClawd()
    {
        var rows = ReferenceScene.ComposeAnsi().Split('\n');
        Assert.True(rows.Length >= ReferenceScene.Height, $"总行数应 >= {ReferenceScene.Height}");
        Assert.Contains(" ▐▛███▜▌", StripAnsi(rows[0]));
        Assert.Contains("▝▜█████▛▘", StripAnsi(rows[1]));
        Assert.Contains("  ▘▘ ▝▝  ", StripAnsi(rows[2]));
    }

    [Fact]
    public void ComposeAnsi_ContainsFullWidthPromptRuleRow()
    {
        var rows = ReferenceScene.ComposeAnsi().Split('\n');
        var promptY = Array.FindIndex(rows, row => StripAnsi(row).StartsWith(new string('─', ReferenceScene.Width)));
        Assert.True(promptY >= 0, "应存在以 80 个 ─ 开头的输入框边框行（py region 提取依赖此特征）");
    }

    /// <summary>剥掉 SGR/CSI 序列只留可见字符（与 py cells() 的 TOKEN 剔除语义对齐）。</summary>
    private static string StripAnsi(string row) =>
        System.Text.RegularExpressions.Regex.Replace(row, @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);

    [Fact]
    public void ComposeAnsi_IsDeterministic()
    {
        Assert.Equal(ReferenceScene.ComposeAnsi(), ReferenceScene.ComposeAnsi());
    }

    [Fact]
    public void ComposeAnsi_Snapshot()
    {
        SnapshotVerifier.Verify(ReferenceScene.ComposeAnsi(), "reference_scene");
    }
}
