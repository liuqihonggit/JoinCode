namespace NativeTui.Tests.Snapshot;

/// <summary>
/// SnapshotVerifier 基建行为测试 — approved/received 快照模式：
/// ① 无 approved 时生成 .received.txt 并失败（提示人工审核）
/// ② approved 与实际一致时通过
/// ③ 不一致时写 .received.txt 供 diff 调试并失败。
/// </summary>
public sealed class SnapshotVerifierTests
{
    /// <summary>构造临时目录（测试自建自清理，非仓库文件）。</summary>
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nativetui-snaptest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Verify_WithoutApproved_WritesReceivedAndFails()
    {
        var dir = TempDir();
        try
        {
            var sourceFile = Path.Combine(dir, "caller.cs");
            Assert.ThrowsAny<Exception>(() => SnapshotVerifier.Verify("actual text", "scene", sourceFile));
            Assert.True(File.Exists(Path.Combine(dir, "scene.received.txt")), "首次运行应生成 .received.txt");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_MatchingApproved_Passes()
    {
        var dir = TempDir();
        try
        {
            var sourceFile = Path.Combine(dir, "caller.cs");
            File.WriteAllText(Path.Combine(dir, "scene.approved.txt"), "actual text");
            SnapshotVerifier.Verify("actual text", "scene", sourceFile);
            Assert.False(File.Exists(Path.Combine(dir, "scene.received.txt")), "一致时不应写 .received.txt");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_MismatchedApproved_WritesReceivedAndFails()
    {
        var dir = TempDir();
        try
        {
            var sourceFile = Path.Combine(dir, "caller.cs");
            File.WriteAllText(Path.Combine(dir, "scene.approved.txt"), "approved text");
            Assert.ThrowsAny<Exception>(() => SnapshotVerifier.Verify("actual text", "scene", sourceFile));
            Assert.True(File.Exists(Path.Combine(dir, "scene.received.txt")), "不一致时应写 .received.txt");
            Assert.Equal("actual text", File.ReadAllText(Path.Combine(dir, "scene.received.txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
