namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// @ 补全索引 TTL 惰性重建测试（S11 遗留#4）— 目录变更可见性（无 FileSystemWatcher 的轻量形态）：
/// TTL 内索引缓存复用（新文件不可见）；超 TTL 下次建议自动重建（新文件可见）；显式 Invalidate 立即重建。
/// 假时钟注入（TickCount64 语义 — 差值判定）确定性推进。
/// </summary>
public sealed class CommandAtFileTtlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nativetui-atttl-" + Guid.NewGuid().ToString("N"));
    private long _now;

    /// <summary>假时钟（可推进）。</summary>
    private long Clock() => _now;

    public CommandAtFileTtlTests()
    {
        Directory.CreateDirectory(_root);
        _now = 100_000;
    }

    [Fact]
    public void Suggest_WithinTtl_CachedIndex_NewFileInvisible()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "x");
        var suggester = new AtFileSuggester(_root, Clock);

        Assert.Single(suggester.Suggest(string.Empty));

        File.WriteAllText(Path.Combine(_root, "b.cs"), "x");
        _now += AtFileSuggester.IndexTimeToLiveMs - 1;

        // TTL 内缓存复用 — b.cs 尚不可见
        Assert.Empty(suggester.Suggest("b"));
    }

    [Fact]
    public void Suggest_BeyondTtl_IndexRebuilt_NewFileVisible()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "x");
        var suggester = new AtFileSuggester(_root, Clock);
        suggester.Suggest(string.Empty);

        File.WriteAllText(Path.Combine(_root, "b.cs"), "x");
        _now += AtFileSuggester.IndexTimeToLiveMs + 1;

        // 超 TTL 惰性重建 — b.cs 可见
        Assert.Equal(["b.cs"], suggester.Suggest("b").Select(s => s.RelativePath).ToArray());
    }

    [Fact]
    public void Invalidate_ImmediateRebuild_RegardlessOfTtl()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "x");
        var suggester = new AtFileSuggester(_root, Clock);
        suggester.Suggest(string.Empty);

        File.WriteAllText(Path.Combine(_root, "c.cs"), "x");
        _now += 1;  // TTL 内

        suggester.Invalidate();
        Assert.Contains("c.cs", suggester.Suggest(string.Empty).Select(s => s.RelativePath));
    }

    [Fact]
    public void RebuiltIndex_FullRefresh_NotAppend()
    {
        // 重建是全量重枚举（旧文件仍可见 + 新文件加入 — 非增量追加）
        File.WriteAllText(Path.Combine(_root, "a.cs"), "x");
        var suggester = new AtFileSuggester(_root, Clock);
        suggester.Suggest(string.Empty);

        File.WriteAllText(Path.Combine(_root, "b.cs"), "x");
        _now += AtFileSuggester.IndexTimeToLiveMs + 1;

        Assert.Equal(2, suggester.Suggest(string.Empty).Count);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理竞态容忍
        }
    }
}
