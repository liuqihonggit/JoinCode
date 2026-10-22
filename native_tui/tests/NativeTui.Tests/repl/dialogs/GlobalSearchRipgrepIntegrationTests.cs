namespace NativeTui.Tests.Repl;

/// <summary>
/// rg 搜索执行器真进程集成测试 — 临时目录建文件跑真 rg（CI/本地有 rg 时验证流式链路：
/// 命中解析/相对化/排除目录剪枝/smart-case 大小写策略/无匹配退出码 1 静默空流/取消传播）。
/// Trait Category=Integration 对齐仓库 ci-unit-tests 过滤惯例（--filter Category!=Integration
/// 跳过）；rg 缺失环境 IsAvailable=false 早退（不红 — 降级路径已由纯函数测试覆盖）。
/// 临时目录不清理（对齐 VectorIndexE2ETests 先例 — OS 临时目录生命周期自理）。
/// </summary>
[Trait("Category", "Integration")]
public sealed class GlobalSearchRipgrepIntegrationTests
{
    /// <summary>命中与排除 — 临时根建目标文件/无关文件/node_modules 依赖文件，NEEDLE 命中
    /// 仅 alpha.cs（node_modules 被 -g 否定 glob 剪枝）；File 相对根归一、行号与行文本精确。</summary>
    [Fact]
    public async Task ExecuteAsync_FindsMatches_AndExcludesDirectories()
    {
        var searcher = new RipgrepSearcher();
        if (!searcher.IsAvailable)
        {
            return;
        }

        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "alpha.cs"), "line1\nNEEDLE found here\nline3\n");
        await File.WriteAllTextAsync(Path.Combine(root, "beta.md"), "# doc\nno match\n");
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "dep.js"), "NEEDLE in dependency\n");

        var matches = await CollectAsync(searcher, root, "NEEDLE");

        var hit = Assert.Single(matches);
        Assert.Equal("alpha.cs", hit.File);
        Assert.Equal(2, hit.Line);
        Assert.Equal("NEEDLE found here", hit.Text);
    }

    /// <summary>无匹配静默 — rg 退出码 1（无命中）是正常形态 → 空流不抛（调用方零打扰语义）。</summary>
    [Fact]
    public async Task ExecuteAsync_NoMatches_ReturnsEmptySilently()
    {
        var searcher = new RipgrepSearcher();
        if (!searcher.IsAvailable)
        {
            return;
        }

        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "only.cs"), "nothing relevant\n");

        var matches = await CollectAsync(searcher, root, "zzz_no_such_token");

        Assert.Empty(matches);
    }

    /// <summary>smart-case 双档 — 小写查询忽略大小写（命中混合大小写行）；含大写查询区分大小写（不命中）。</summary>
    [Fact]
    public async Task ExecuteAsync_SmartCase_InsensitiveForLowercaseQuery()
    {
        var searcher = new RipgrepSearcher();
        if (!searcher.IsAvailable)
        {
            return;
        }

        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "mixed.cs"), "MixedCase Needle line\n");

        var lowercase = await CollectAsync(searcher, root, "needle");
        var hit = Assert.Single(lowercase);
        Assert.Equal(1, hit.Line);

        var uppercase = await CollectAsync(searcher, root, "NEEDLE");
        Assert.Empty(uppercase);
    }

    /// <summary>取消杀进程树 — 令牌已取消的枚举即取消异常族传播（防抖重触发语义：旧搜索中断不残留；
    /// OCE 派生 TaskCanceledException 同纳 — ThrowsAnyAsync 宽匹配）。</summary>
    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var searcher = new RipgrepSearcher();
        if (!searcher.IsAvailable)
        {
            return;
        }

        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "alpha.cs"), "NEEDLE line\n");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(searcher, root, "NEEDLE", new CancellationToken(canceled: true)));
    }

    /// <summary>临时根目录 — GetTempPath + Guid 防撞（不清理，对齐仓库集成测试先例）。</summary>
    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gs_rg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>收集执行命中流（显式令牌透传形态）。</summary>
    private static async Task<List<SearchMatch>> CollectAsync(
        RipgrepSearcher searcher,
        string root,
        string query,
        CancellationToken cancellationToken = default)
    {
        var matches = new List<SearchMatch>();
        await foreach (var match in searcher.ExecuteAsync(root, query, cancellationToken))
        {
            matches.Add(match);
        }

        return matches;
    }
}
