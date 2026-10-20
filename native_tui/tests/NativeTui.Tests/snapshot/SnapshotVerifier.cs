namespace NativeTui.Tests.Snapshot;

/// <summary>
/// 轻量快照比对器 — 与 .approved.txt 比对，首次生成 .received.txt 供人工审核。
/// 零外部依赖，AOT 友好。对齐 ApprovalTests/Verify 的 received/approved 模式。
/// 行尾无关比对（CRLF 折叠为 LF）— approved 文件经 git autocrlf 物化或 Windows 编辑器
/// 保存会带 CRLF，实际输出统一 LF，裸 Ordinal 比较会被行尾差异击穿（行尾非快照语义）。
/// </summary>
public static class SnapshotVerifier
{
    /// <summary>
    /// 验证实际输出与批准的快照一致（两侧行尾先归一化 CRLF → LF）。
    /// 首次运行（无 .approved.txt）时生成 .received.txt 并断言失败，提示人工审核。
    /// 后续运行比对内容，不匹配时写 .received.txt 供 diff 调试。
    /// </summary>
    /// <param name="actual">实际输出文本。</param>
    /// <param name="snapshotName">快照名（不含扩展名，自动追加 .approved.txt/.received.txt）。</param>
    /// <param name="sourceFile">调用方源文件路径（由 CallerFilePath 自动注入）。</param>
    public static void Verify(string actual, string snapshotName, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var dir = Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory;
        var approvedPath = Path.Combine(dir, $"{snapshotName}.approved.txt");
        var receivedPath = Path.Combine(dir, $"{snapshotName}.received.txt");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(receivedPath, NormalizeLineEndings(actual));
            Assert.Fail($"快照未批准: 已生成 {receivedPath}。审核内容后将其重命名为 {snapshotName}.approved.txt。");
        }

        var approved = NormalizeLineEndings(File.ReadAllText(approvedPath));
        if (!string.Equals(approved, NormalizeLineEndings(actual), StringComparison.Ordinal))
        {
            File.WriteAllText(receivedPath, NormalizeLineEndings(actual));
            Assert.Fail($"快照不匹配: 实际输出已写入 {receivedPath}，请与 {approvedPath} 对比。");
        }
    }

    /// <summary>行尾归一化 — CRLF 折叠为 LF（快照语义与行尾无关；孤立 CR 保留为帧数据）。</summary>
    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");
}
