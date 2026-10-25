namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// ShellCompleter 测试（TASK074 D3）— 临时目录放假可执行文件（非 mock 真实 IO）：
/// 前缀过滤、空输入空清单、无匹配空清单、取首 token、上限 20、大小写不敏感、多目录去重。
/// </summary>
public sealed class ShellCompleterTests
{
    /// <summary>创建隔离临时目录放假可执行文件。</summary>
    private static string CreateTempPathDir(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nativetui-shellcomp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in fileNames)
        {
            File.WriteAllText(Path.Combine(dir, name), "x");
        }

        return dir;
    }

    [Fact]
    public void Complete_ByPrefix_ReturnsMatches()
    {
        var dir = CreateTempPathDir("git", "grep", "ls", "cat");
        try
        {
            var completer = new ShellCompleter([dir]);
            var suggestions = completer.Complete("g");
            var names = suggestions.Select(static s => s.Name).ToList();
            Assert.Contains("git", names);
            Assert.Contains("grep", names);
            Assert.DoesNotContain("ls", names);
            Assert.DoesNotContain("cat", names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_EmptyInput_ReturnsEmpty()
    {
        var dir = CreateTempPathDir("git", "ls");
        try
        {
            var completer = new ShellCompleter([dir]);
            Assert.Empty(completer.Complete(string.Empty));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_NoMatch_ReturnsEmpty()
    {
        var dir = CreateTempPathDir("git", "ls");
        try
        {
            var completer = new ShellCompleter([dir]);
            Assert.Empty(completer.Complete("zzz"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_TakesFirstToken_IgnoresArgs()
    {
        var dir = CreateTempPathDir("git", "grep");
        try
        {
            var completer = new ShellCompleter([dir]);
            var suggestions = completer.Complete("git st");
            var names = suggestions.Select(static s => s.Name).ToList();
            Assert.Contains("git", names);
            Assert.DoesNotContain("grep", names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_MoreThan20_LimitsTo20()
    {
        var files = Enumerable.Range(0, 30).Select(static i => "cmd" + i.ToString("D2")).ToArray();
        var dir = CreateTempPathDir(files);
        try
        {
            var completer = new ShellCompleter([dir]);
            var suggestions = completer.Complete("cmd");
            Assert.Equal(20, suggestions.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_CaseInsensitive_MatchesLowerFromUpper()
    {
        var dir = CreateTempPathDir("GIT", "grep");
        try
        {
            var completer = new ShellCompleter([dir]);
            var suggestions = completer.Complete("git");
            Assert.Contains(suggestions, static s => s.Name == "GIT");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Complete_MultipleDirs_Deduplicates()
    {
        var dir1 = CreateTempPathDir("git", "ls");
        var dir2 = CreateTempPathDir("git", "cat");
        try
        {
            var completer = new ShellCompleter([dir1, dir2]);
            var suggestions = completer.Complete("git");
            Assert.Single(suggestions);
        }
        finally
        {
            Directory.Delete(dir1, recursive: true);
            Directory.Delete(dir2, recursive: true);
        }
    }

    [Fact]
    public void Complete_NonExistentDir_SkipsGracefully()
    {
        var missing = Path.Combine(Path.GetTempPath(), "nativetui-shellcomp-missing-" + Guid.NewGuid().ToString("N"));
        var completer = new ShellCompleter([missing]);
        Assert.Empty(completer.Complete("g"));
    }
}
