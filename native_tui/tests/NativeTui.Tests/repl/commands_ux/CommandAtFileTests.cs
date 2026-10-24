namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// AtFileSuggester 测试 — 临时工作区真实文件树（非 mock）：空前缀浅路径优先、
/// 大小写不敏感包含过滤、忽略目录整棵跳过、上限 20、索引缓存与 Invalidate 重建、
/// 根目录不存在降级空清单、参数校验。
/// </summary>
public sealed class CommandAtFileTests
{
    /// <summary>创建隔离临时工作区（用例结束统一清理）。</summary>
    private static string CreateTempWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "nativetui-atfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>写入相对路径文件（先创建父目录 — File.WriteAllText 不自建目录）。</summary>
    private static void WriteFile(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(fullPath, "x");
    }

    /// <summary>标准工作区（覆盖浅/深层路径与各忽略目录）。</summary>
    private static string StandardWorkspace()
    {
        var root = CreateTempWorkspace();
        WriteFile(root, "readme.md");
        WriteFile(root, "a.txt");
        WriteFile(root, "src/main.cs");
        WriteFile(root, "src/util/helper.cs");
        WriteFile(root, "docs/guide.md");
        WriteFile(root, ".git/config");
        WriteFile(root, "node_modules/pkg/index.js");
        WriteFile(root, "bin/intermediate.dll");
        return root;
    }

    /// <summary>空前缀：全部文件浅路径优先 + 字母序，忽略目录不出现。</summary>
    [Fact]
    public void Suggest_EmptyPrefix_ShallowPathsFirst()
    {
        var root = StandardWorkspace();
        try
        {
            var suggestions = new AtFileSuggester(root).Suggest(string.Empty);
            Assert.Equal(
                ["a.txt", "readme.md", "src/main.cs", "docs/guide.md", "src/util/helper.cs"],
                suggestions.Select(suggestion => suggestion.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>前缀过滤：大小写不敏感包含匹配（Windows 路径比较不敏感；Unix 用原样大小写）。</summary>
    [Fact]
    public void Suggest_PrefixFilter_CaseInsensitiveContains()
    {
        var root = StandardWorkspace();
        try
        {
            var prefix = OperatingSystem.IsWindows() ? "MAIN" : "main";
            var suggestions = new AtFileSuggester(root).Suggest(prefix);
            Assert.Equal(["src/main.cs"], suggestions.Select(suggestion => suggestion.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>子目录名片段匹配：路径包含（不只文件名前缀）。</summary>
    [Fact]
    public void Suggest_DirectorySegment_MatchesFullPath()
    {
        var root = StandardWorkspace();
        try
        {
            var suggestions = new AtFileSuggester(root).Suggest("util");
            Assert.Equal(["src/util/helper.cs"], suggestions.Select(suggestion => suggestion.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>忽略目录：.git/node_modules/bin 整棵不进索引。</summary>
    [Fact]
    public void Suggest_IgnoredDirectories_Excluded()
    {
        var root = StandardWorkspace();
        try
        {
            var paths = new AtFileSuggester(root).Suggest(string.Empty)
                .Select(suggestion => suggestion.RelativePath)
                .ToList();
            Assert.DoesNotContain(paths, path => path.Contains(".git/") || path.StartsWith(".git"));
            Assert.DoesNotContain(paths, path => path.Contains("node_modules"));
            Assert.DoesNotContain(paths, path => path.Contains("bin/"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>上限 20：超出截断（30 个文件 → 20 条建议）。</summary>
    [Fact]
    public void Suggest_LimitsToMaxSuggestions()
    {
        var root = CreateTempWorkspace();
        try
        {
            foreach (var index in Enumerable.Range(0, 30))
            {
                WriteFile(root, $"file{index:D2}.txt");
            }

            Assert.Equal(AtFileSuggester.MaxSuggestions, new AtFileSuggester(root).Suggest(string.Empty).Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>索引缓存：缓存期内新增文件不可见（重建由 Invalidate 显式触发）。</summary>
    [Fact]
    public void Suggest_CachesUntilInvalidated()
    {
        var root = CreateTempWorkspace();
        try
        {
            var suggester = new AtFileSuggester(root);
            WriteFile(root, "early.txt");
            Assert.Contains(suggester.Suggest(string.Empty), suggestion => suggestion.RelativePath == "early.txt");

            WriteFile(root, "late.txt");
            Assert.DoesNotContain(suggester.Suggest(string.Empty), suggestion => suggestion.RelativePath == "late.txt");

            suggester.Invalidate();
            Assert.Contains(suggester.Suggest(string.Empty), suggestion => suggestion.RelativePath == "late.txt");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>根目录不存在：降级空清单（不抛 — 工作区未打开容忍）。</summary>
    [Fact]
    public void Suggest_MissingRoot_ReturnsEmpty()
    {
        var suggester = new AtFileSuggester(Path.Combine(Path.GetTempPath(), "nativetui-atfile-absent-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(suggester.Suggest(string.Empty));
    }

    /// <summary>无匹配前缀：空清单。</summary>
    [Fact]
    public void Suggest_NoMatch_ReturnsEmpty()
    {
        var root = StandardWorkspace();
        try
        {
            Assert.Empty(new AtFileSuggester(root).Suggest("zzz-no-such-file"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>参数校验：空根路径抛 ArgumentException。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_EmptyRootPath_Throws(string rootPath)
    {
        Assert.Throws<ArgumentException>(() => new AtFileSuggester(rootPath));
    }

    /// <summary>respectGitignore 接线 — isIgnored 委托跳过 gitignore 匹配的文件（对齐 TS file picker 语义）。</summary>
    [Fact]
    public void Suggest_IsIgnoredDelegate_SkipsIgnoredFiles()
    {
        var root = StandardWorkspace();
        try
        {
            // 模拟 .gitignore 忽略 docs/ 目录
            var sut = new AtFileSuggester(root, isIgnored: rel => rel.StartsWith("docs/", StringComparison.OrdinalIgnoreCase));

            var suggestions = sut.Suggest("");

            Assert.All(suggestions, s => Assert.DoesNotContain("docs/", s.RelativePath));
            Assert.DoesNotContain(suggestions, s => s.RelativePath.StartsWith("docs/"));
            Assert.Contains(suggestions, s => s.RelativePath == "readme.md");
            Assert.Contains(suggestions, s => s.RelativePath == "a.txt");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>isIgnored null = 未注入零行为变化（仅硬编码忽略目录生效）。</summary>
    [Fact]
    public void Suggest_NullIsIgnored_KeepsDefaultBehavior()
    {
        var root = StandardWorkspace();
        try
        {
            var sut = new AtFileSuggester(root, isIgnored: null);
            var suggestions = sut.Suggest("");

            Assert.Contains(suggestions, s => s.RelativePath == "docs/guide.md");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>路径分隔符：统一 / 形态（Windows 反斜杠归一化）。</summary>
    [Fact]
    public void Suggest_PathsUseForwardSlashes()
    {
        var root = StandardWorkspace();
        try
        {
            Assert.All(new AtFileSuggester(root).Suggest("src"),
                suggestion => Assert.DoesNotContain('\\', suggestion.RelativePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
