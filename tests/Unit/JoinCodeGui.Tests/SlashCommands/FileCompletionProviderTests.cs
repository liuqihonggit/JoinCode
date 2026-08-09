using FluentAssertions;

using JoinCode.Gui.SlashCommands;

namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// FileCompletionProvider 单元测试 — 验证当前目录文件扫描与前缀过滤。
/// </summary>
public class FileCompletionProviderTests
{
    [Fact]
    public void GetFiles_EmptyPrefix_ReturnsCurrentDirEntries()
    {
        var files = FileCompletionProvider.GetFiles("");
        files.Should().NotBeEmpty();
    }

    [Fact]
    public void GetFiles_WithPrefix_FiltersResultsCaseInsensitive()
    {
        var baseDir = AppContext.BaseDirectory;
        var all = FileCompletionProvider.GetFiles("", baseDir);
        all.Should().NotBeEmpty();
        var prefix = all[0].Name[..1];
        var filtered = FileCompletionProvider.GetFiles(prefix, baseDir);
        filtered.Should().NotBeEmpty();
        filtered.All(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void GetFiles_NonMatchingPrefix_ReturnsEmpty()
    {
        var baseDir = AppContext.BaseDirectory;
        var files = FileCompletionProvider.GetFiles("zzz_nonexistent_prefix_xyz", baseDir);
        files.Should().BeEmpty();
    }

    [Fact]
    public void GetFiles_LimitedTo50Results()
    {
        var baseDir = AppContext.BaseDirectory;
        var files = FileCompletionProvider.GetFiles("", baseDir);
        (files.Count <= 50).Should().BeTrue();
    }
}
