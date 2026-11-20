namespace Integration.Tests.McpTools;

/// <summary>
/// 隔离宿主单测 — 验证 TASK072 C 项：每轮独占 patrol-{Guid:N} 目录、零磁盘残留、绝对路径注入。
/// </summary>
public sealed class IsolatedTestHostTests {
    [Fact]
    public async Task EachHost_GetUniquePatrolTempDir() {
        await using var h1 = new IsolatedTestHost(disposeProvider: false);
        await using var h2 = new IsolatedTestHost(disposeProvider: false);

        h1.TempDir.Should().NotBe(h2.TempDir, "每个宿主独占不同 patrol 目录，消除类间并行踩踏");
        Path.GetFileName(h1.TempDir).Should().StartWith("patrol-");
        Path.IsPathRooted(h1.TempDir).Should().BeTrue();
    }

    [Fact]
    public async Task InMemoryFileSystem_RoundTripsWithoutPhysicalDiskProbe() {
        const string fileName = "patrol-memory-probe.txt";
        await using var host = new IsolatedTestHost(disposeProvider: false);

        // 宿主注入的 IFileSystem 必须是内存实现（物理零残留由此类型保证；
        // JCC9001 禁测试直接调 File.*，磁盘探测交由 python 门禁，不在 C# 测试内做）。
        host.FileSystem.Should().BeOfType<IO.FileSystem.InMemoryFileSystem>();

        var path = Path.Combine(host.TempDir, fileName);
        await host.FileSystem.WriteAllTextAsync(path, "memory only", CancellationToken.None);
        (await host.FileSystem.ReadAllTextAsync(path, CancellationToken.None)).Should().Be("memory only");
    }

    [Fact]
    public async Task GeneratedArgs_LandInsideOwnPatrolDir() {
        await using var h1 = new IsolatedTestHost(disposeProvider: false);
        await using var h2 = new IsolatedTestHost(disposeProvider: false);
        var tool = new ToolInfo {
            Name = "file_write",
            InputSchema = new ToolSchema {
                Required = ["file_path"],
                Properties = new() { ["file_path"] = new ToolSchemaProperty { Type = "string" } },
            },
        };

        var args1 = ArgumentGenerator.GenerateDefaultArguments(tool, h1.TempDir);
        var args2 = ArgumentGenerator.GenerateDefaultArguments(tool, h2.TempDir);

        args1["file_path"].GetString().Should().StartWith(h1.TempDir);
        args2["file_path"].GetString().Should().StartWith(h2.TempDir);
        args1["file_path"].GetString().Should().NotBe(args2["file_path"].GetString());
    }
}
