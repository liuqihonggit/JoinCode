namespace Abs.Tests.Utils;

/// <summary>
/// InMemoryFileSystem 搜索模式契约测试 — bug 修复：EnumerateDirectories/EnumerateFiles 的
/// searchPattern 参数此前被完全忽略（返回全部子项）——违反 IFileSystem 契约，
/// 实际事故：HousekeepingService.CleanupOldMessageFiles 枚举 "mcp-logs-*" 误扫 sessions
/// 目录删除用户会话文件。Win32 语义：* 与 ? 通配。
/// </summary>
public sealed class InMemorySearchPatternTests {
    private readonly Testing.Common.Services.InMemoryFileSystem _fs = new();

    public InMemorySearchPatternTests() {
        _fs.CreateDirectory("/root/sessions");
        _fs.CreateDirectory("/root/mcp-logs-a");
        _fs.CreateDirectory("/root/mcp-logs-b");
        _fs.CreateDirectory("/root/other");
        _fs.WriteAllText("/root/sessions/old-session.json", "{}");
        _fs.WriteAllText("/root/mcp-logs-a/a.txt", "x");
        _fs.WriteAllText("/root/other/o.txt", "x");
    }

    [Fact]
    public void EnumerateDirectories_前缀通配_只匹配前缀目录() {
        var dirs = _fs.EnumerateDirectories("/root", "mcp-logs-*", SearchOption.TopDirectoryOnly).ToList();

        dirs.Should().HaveCount(2, "mcp-logs-* 应只匹配两个 mcp-logs 前缀目录");
        dirs.Should().NotContain(d => d.Contains("sessions"), "sessions 不应被 mcp-logs-* 匹配（事故根因）");
        dirs.Should().NotContain(d => d.Contains("other"));
    }

    [Fact]
    public void EnumerateDirectories_全通配_返回全部子目录() {
        var dirs = _fs.EnumerateDirectories("/root", "*", SearchOption.TopDirectoryOnly).ToList();

        dirs.Should().HaveCount(4);
    }

    [Fact]
    public void EnumerateDirectories_精确名_只匹配同名目录() {
        var dirs = _fs.EnumerateDirectories("/root", "sessions", SearchOption.TopDirectoryOnly).ToList();

        dirs.Should().HaveCount(1);
        dirs[0].Should().Contain("sessions");
    }

    [Fact]
    public void EnumerateFiles_扩展名通配_只匹配扩展名() {
        var files = _fs.EnumerateFiles("/root/sessions", "*.json", SearchOption.TopDirectoryOnly).ToList();

        files.Should().HaveCount(1);
        files[0].Should().Contain("old-session.json");
    }

    [Fact]
    public void EnumerateFiles_全通配_返回全部文件() {
        var files = _fs.EnumerateFiles("/root/other", "*", SearchOption.TopDirectoryOnly).ToList();

        files.Should().HaveCount(1);
    }
}
