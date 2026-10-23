namespace Infra.Tests.FileOps;

/// <summary>
/// FileHistoryService fileCheckpointingEnabled 门控测试 — 对齐 TS fileHistoryEnabled()
/// （fileCheckpointingEnabled !== false）：false 时 BackupBeforeWriteAsync 跳过备份静默返回 null。
/// InMemoryFileSystem 消除磁盘 IO；JCC_APP_DATA_FOLDER 隔离防污染真实 ~/.jcc/file-history/。
/// </summary>
[Collection("AppDataConstantsCollection")]
public sealed class FileHistoryCheckpointingTests : IDisposable {
    private readonly EnvVarScope _envScope;
    private bool _disposed;

    public FileHistoryCheckpointingTests() {
        _envScope = EnvVarScope.Set(JccEnvVarEnumConstants.AppDataFolder, $"/test/file_history_{Guid.NewGuid().ToString("N")[..8]}");
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _envScope.DisposeSafe();
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    /// <summary>跨平台预规范化 — Write 与 Backup 走同一规范化路径（服务内部 Path.GetFullPath 双平台一致命中）。</summary>
    private static string PlatformFile() => Path.GetFullPath("/proj/a.txt");

    [Fact]
    public async Task BackupBeforeWrite_CheckpointingDisabled_ReturnsNullAndSkipsBackup() {
        await using var fs = new InMemoryFileSystem();
        var file = PlatformFile();
        await fs.WriteAllTextAsync(file, "original");
        await using var sut = new global::IO.FileHistoryService(fs, workflowConfig: new WorkflowConfig { FileCheckpointingEnabled = false });

        var result = await sut.BackupBeforeWriteAsync(file);

        result.Should().BeNull("fileCheckpointingEnabled=false 应跳过备份");
        HasNoBackups(fs).Should().BeTrue("不应产生任何备份文件");
    }

    [Fact]
    public async Task BackupBeforeWrite_NoWorkflowConfig_DefaultsEnabled() {
        await using var fs = new InMemoryFileSystem();
        var file = PlatformFile();
        await fs.WriteAllTextAsync(file, "original");
        await using var sut = new global::IO.FileHistoryService(fs);

        var result = await sut.BackupBeforeWriteAsync(file);

        result.Should().NotBeNull("默认 fileCheckpointingEnabled=true 保持写前备份行为");
        result.Should().Contain(AppDataConstants.Paths.FileHistoryDirectory, "备份应落在 file-history 目录");
    }

    private static bool HasNoBackups(InMemoryFileSystem fs) {
        var dir = AppDataConstants.Paths.FileHistoryDirectory;
        return !fs.DirectoryExists(dir) || !fs.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
    }
}
