namespace Core.Tests;

/// <summary>
/// WriteDefense 端到端集成测试 — 真实 PhysicalFileSystem + 真实 WriteDefenseService + 真实 FileStateCache。
/// 单进程内完成 read→rebase→write 链路验证，非 mock，真实暴露运行行为。
/// </summary>
public class WriteDefenseEndToEndTests {
    private static WriteDefenseService CreateDefense(IFileSystem fs, IFileStateCache cache) {
        return new WriteDefenseService(
            new SecretGuardNode(),
            new FileBackupNode(fs),
            new WriteNotifyNode(fs),
            new SandboxGuardNode(),
            new FileStateGuardNode(fs, cache),
            new FormatValidatorNode(fs));
    }

    private static async ValueTask<(WriteDefenseContext Ctx, ToolResult? Rejection)> RunWriteDefenseAsync(
        WriteDefenseService defense, string filePath, string content, CancellationToken ct = default) {
        return await WriteDefense
            .Begin(filePath, content, FileOperationType.Write, "writing")
            .Then(defense.RejectUncPath)
            .Then(defense.ResolveSandboxAsync)
            .Then(defense.CheckTeamMemSecrets)
            .Then(defense.RequireReadBeforeWrite)
            .Then(defense.GuardStaleWriteAsync)
            .Then(defense.BackupBeforeWriteAsync)
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// rebase 场景端到端：read → rebase 触碰 mtime（内容不变）→ write 链路 → 放行。
    /// 用 fs.WriteAllText 相同内容模拟 rebase（mtime 变新，内容不变）。
    /// </summary>
    [Fact]
    public async Task E2E_Rebase_TimestampChangedContentSame_DefensePasses() {
        await using var fs = new PhysicalFileSystem();
        await using var cache = new FileStateCache();
        var filePath = Path.Combine(Path.GetTempPath(), $"e2e_rebase_{Guid.NewGuid():N}.txt");
        const string content = "original content unchanged";
        try {
            await fs.WriteAllText(filePath, content);
            var readMs = new DateTimeOffset(fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            cache.RecordRead(filePath, content, readMs - 10_000); // 模拟 10s 前读的

            // rebase：重新写入相同内容，mtime 变新
            await fs.WriteAllText(filePath, content);

            var defense = CreateDefense(fs, cache);
            var (_, rejection) = await RunWriteDefenseAsync(defense, filePath, "new content");

            Assert.Null(rejection); // 内容不变，兜底放行
        } finally {
            if (fs.FileExists(filePath)) fs.DeleteFile(filePath);
        }
    }

    /// <summary>
    /// rebase 内容变更端到端：read → 其他分支改内容 → write 链路 → 拒绝（脏写）。
    /// </summary>
    [Fact]
    public async Task E2E_Rebase_ContentChanged_DefenseRejectsStale() {
        await using var fs = new PhysicalFileSystem();
        await using var cache = new FileStateCache();
        var filePath = Path.Combine(Path.GetTempPath(), $"e2e_stale_{Guid.NewGuid():N}.txt");
        const string readContent = "AI read this";
        try {
            await fs.WriteAllText(filePath, readContent);
            var readMs = new DateTimeOffset(fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            cache.RecordRead(filePath, readContent, readMs - 10_000);

            // 其他分支改了内容
            await fs.WriteAllText(filePath, "rebase changed this");

            var defense = CreateDefense(fs, cache);
            var (_, rejection) = await RunWriteDefenseAsync(defense, filePath, "new content");

            Assert.NotNull(rejection); // 内容变了，脏写拒绝
        } finally {
            if (fs.FileExists(filePath)) fs.DeleteFile(filePath);
        }
    }

    /// <summary>
    /// 未读端到端：不 read → write 链路 → RequireReadBeforeWrite 拒绝。
    /// </summary>
    [Fact]
    public async Task E2E_NotRead_DefenseRejectsNotRead() {
        await using var fs = new PhysicalFileSystem();
        await using var cache = new FileStateCache();
        var filePath = Path.Combine(Path.GetTempPath(), $"e2e_noread_{Guid.NewGuid():N}.txt");
        try {
            await fs.WriteAllText(filePath, "content");

            var defense = CreateDefense(fs, cache);
            var (_, rejection) = await RunWriteDefenseAsync(defense, filePath, "new content");

            Assert.NotNull(rejection); // 未读，拒绝
        } finally {
            if (fs.FileExists(filePath)) fs.DeleteFile(filePath);
        }
    }

    /// <summary>
    /// 部分读端到端：read with offset/limit → write 链路 → RequireReadBeforeWrite 拒绝（IsPartialView）。
    /// </summary>
    [Fact]
    public async Task E2E_PartialRead_DefenseRejectsNotRead() {
        await using var fs = new PhysicalFileSystem();
        await using var cache = new FileStateCache();
        var filePath = Path.Combine(Path.GetTempPath(), $"e2e_partial_{Guid.NewGuid():N}.txt");
        try {
            await fs.WriteAllText(filePath, "line1\nline2\nline3\nline4\nline5");
            var readMs = new DateTimeOffset(fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            cache.RecordRead(filePath, "line2\nline3", readMs - 10_000, offset: 2, limit: 2);

            var defense = CreateDefense(fs, cache);
            var (_, rejection) = await RunWriteDefenseAsync(defense, filePath, "new content");

            Assert.NotNull(rejection); // 部分读，视为未读，拒绝
        } finally {
            if (fs.FileExists(filePath)) fs.DeleteFile(filePath);
        }
    }
}
