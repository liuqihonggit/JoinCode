namespace Tui.Tests.Rendering;

/// <summary>
/// 轻量快照比对器 — 与 .approved.txt 比对，首次生成 .received.txt 供人工审核。
/// 零外部依赖，AOT 友好。对齐 ApprovalTests/Verify 的 received/approved 模式。
/// </summary>
public static class SnapshotVerifier
{
    /// <summary>
    /// 验证实际输出与批准的快照一致。
    /// 首次运行（无 .approved.txt）时生成 .received.txt 并断言失败，提示人工审核。
    /// 后续运行比对内容，不匹配时写 .received.txt 供 diff 调试。
    /// </summary>
    /// <param name="actual">实际输出文本。</param>
    /// <param name="snapshotName">快照名（不含扩展名，自动追加 .approved.txt/.received.txt）。</param>
    /// <param name="sourceFile">调用方源文件路径（由 CallerFilePath 自动注入）。</param>
    public static void Verify(string actual, string snapshotName, [CallerFilePath] string sourceFile = "")
    {
        var dir = Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory;
        var approvedPath = Path.Combine(dir, $"{snapshotName}.approved.txt");
        var receivedPath = Path.Combine(dir, $"{snapshotName}.received.txt");

#pragma warning disable JCC9001
        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"快照未批准: 已生成 {receivedPath}。审核内容后将其重命名为 {snapshotName}.approved.txt。");
        }

        var approved = File.ReadAllText(approvedPath);
        if (!string.Equals(approved, actual, StringComparison.Ordinal))
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"快照不匹配: 实际输出已写入 {receivedPath}，请与 {approvedPath} 对比。");
        }
#pragma warning restore JCC9001
    }
}
