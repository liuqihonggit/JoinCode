namespace NativeTui.Tests.Repl;

/// <summary>
/// IssueReferenceExpander 测试 — 完整形态/短形态上下文/数字边界/误报防御（域名路径/
/// URL 锚点/前缀词字符）/多实例区间文本序/区间不含前缀/URL 组装/ECMAScript ASCII 边界
/// （中文前缀与数字后中文 — C# 默认 Unicode \w 会吞边界，此处锁定 JS 语义）。
/// 基准 utils/markdown.ts:289-308 ISSUE_REF_PATTERN 行为逐项锁定 + 短形态显式上下文恢复语义。
/// </summary>
public sealed class IssueReferenceExpanderTests
{
    [Fact]
    public void Qualified_SingleReference_RangeAndUrl()
    {
        const string text = "修复见 anthropics/claude-code#42 已关闭";
        var references = IssueReferenceExpander.Expand(text).ToList();

        // 区间不含前缀字符（"见 " 的空格保留在区间外 — 消费方替换时前缀原样保留，基准 prefix 组同语义）
        var reference = Assert.Single(references);
        Assert.Equal("anthropics/claude-code#42", text[reference.Span]);
        Assert.Equal("https://github.com/anthropics/claude-code/issues/42", reference.Url);
    }

    [Fact]
    public void Qualified_AtTextStart_Matches()
    {
        // ^ 前缀分支 — 串首引用无需前导字符
        const string text = "anthropics/claude-code#1 是首个";
        var references = IssueReferenceExpander.Expand(text).ToList();

        Assert.Equal("anthropics/claude-code#1", text[Assert.Single(references).Span]);
    }

    [Fact]
    public void Qualified_RepoWithDots_Matched()
    {
        // repo 段允许点（如 cc.kurs.web）— owner 段禁点防域名误报（基准注释同译）
        const string text = "owner/cc.kurs.web#7 已修复";
        var references = IssueReferenceExpander.Expand(text).ToList();

        var reference = Assert.Single(references);
        Assert.Equal("owner/cc.kurs.web#7", text[reference.Span]);
        Assert.Equal("https://github.com/owner/cc.kurs.web/issues/7", reference.Url);
    }

    [Fact]
    public void Qualified_HostnameLikePath_NoFalsePositive()
    {
        // docs.github.io/guide#42 不误报 — owner 段禁点 + 前缀排除 . 与 /（基准设计意图）
        const string text = "见 docs.github.io/guide#42 文档";
        Assert.Empty(IssueReferenceExpander.Expand(text));
    }

    [Fact]
    public void Bare_WithDefaultOwnerRepo_RangeAndUrl()
    {
        const string text = "见 #42 讨论";
        var references = IssueReferenceExpander.Expand(text, "anthropics/claude-code").ToList();

        var reference = Assert.Single(references);
        Assert.Equal("#42", text[reference.Span]);
        Assert.Equal("https://github.com/anthropics/claude-code/issues/42", reference.Url);
    }

    [Fact]
    public void Bare_AtTextStart_Matches()
    {
        // ^ 零宽前缀 — # 位于串首时区间起点为 0（num 组起点 - 1 反推）
        const string text = "#42 顶部";
        var references = IssueReferenceExpander.Expand(text, "o/r").ToList();

        Assert.Equal("#42", text[Assert.Single(references).Span]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bare_WithoutContext_Skipped(string? defaultOwnerRepo)
    {
        // 无上下文短形态跳过 — 基准删除裸形态语义（猜测当前仓库常猜错）的保守等价物
        Assert.Empty(IssueReferenceExpander.Expand("见 #42 讨论", defaultOwnerRepo));
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("a b/c")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    public void Bare_InvalidOwnerRepoFormat_Skipped(string defaultOwnerRepo)
    {
        // 非法 owner/repo 格式不展开短形态 — 防配置错误产出非法 URL（安全降级）
        Assert.Empty(IssueReferenceExpander.Expand("见 #42 讨论", defaultOwnerRepo));
    }

    [Fact]
    public void Bare_UrlPathAnchor_NotMatched()
    {
        // # 前是 / — 前缀排除类命中，URL 锚点不误报
        Assert.Empty(IssueReferenceExpander.Expand("https://example.com/#42", "o/r"));
    }

    [Fact]
    public void Bare_WordPreceded_NotMatched()
    {
        // # 前是词字符（foo#42）— 非引用位置
        Assert.Empty(IssueReferenceExpander.Expand("foo#42", "o/r"));
    }

    [Fact]
    public void DigitBoundary_TrailingWordChar_Skipped()
    {
        // \b 失败 — 数字后紧跟字母非引用（如十六进制/标识符片段）
        Assert.Empty(IssueReferenceExpander.Expand("o/r#123abc"));
        Assert.Empty(IssueReferenceExpander.Expand("#42x", "o/r"));
    }

    [Fact]
    public void Multiple_References_TextOrder()
    {
        // 多实例：两完整形态 + 一短形态，输出按区间起点文本序
        const string text = "a/b#1 与 c/d#2 与 #3";
        var references = IssueReferenceExpander.Expand(text, "o/r").ToList();

        Assert.Equal(
            new[] { "a/b#1", "c/d#2", "#3" },
            references.Select(reference => text[reference.Span]).ToArray());
        Assert.Equal(
            new[]
            {
                "https://github.com/a/b/issues/1",
                "https://github.com/c/d/issues/2",
                "https://github.com/o/r/issues/3",
            },
            references.Select(reference => reference.Url).ToArray());
    }

    [Fact]
    public void Mixed_BareBeforeQualified_TextOrder()
    {
        // 短形态在前、完整形态在后 — 归并后仍文本序（锁 OrderBy 而非 Concat 直出序）
        const string text = "#1 见 o/r#2";
        var references = IssueReferenceExpander.Expand(text, "o/r").ToList();

        Assert.Equal(
            new[] { "#1", "o/r#2" },
            references.Select(reference => text[reference.Span]).ToArray());
    }

    [Fact]
    public void Qualified_ReferenceNotDoubleCountedAsBare()
    {
        // 完整形态与短形态天然互斥 — o/r#1 不被短形态二次计数（# 前是 repo 尾字符 ∈ [\w.-]）
        const string text = "o/r#1";
        var references = IssueReferenceExpander.Expand(text, "o/r").ToList();

        Assert.Equal("o/r#1", text[Assert.Single(references).Span]);
    }

    [Fact]
    public void ChineseText_BareReference_Matched()
    {
        // ECMAScript ASCII \w — 中文非词字符，作合法前缀（C# 默认 Unicode \w 会判中文为 \w 前缀失败）
        const string text = "修复#123 完成";
        var references = IssueReferenceExpander.Expand(text, "o/r").ToList();

        var reference = Assert.Single(references);
        Assert.Equal("#123", text[reference.Span]);
        Assert.Equal("https://github.com/o/r/issues/123", reference.Url);
    }

    [Fact]
    public void ChineseText_AfterDigits_BoundaryHolds()
    {
        // 数字后紧跟中文 — ASCII \b 边界成立（Unicode \w 下会因中文属 \w 而边界消失不匹配）
        const string text = "o/r#123中文说明";
        var references = IssueReferenceExpander.Expand(text).ToList();

        Assert.Equal("o/r#123", text[Assert.Single(references).Span]);
    }

    [Fact]
    public void LineBreak_AsPrefix_Matched()
    {
        // 多行文本 — 换行符作普通前缀字符（^ 仅匹配串首，对齐基准无 m 标志）
        const string text = "第一行\no/r#9 第二行";
        var references = IssueReferenceExpander.Expand(text).ToList();

        Assert.Equal("o/r#9", text[Assert.Single(references).Span]);
    }

    [Fact]
    public void EmptyText_NoReferences()
    {
        Assert.Empty(IssueReferenceExpander.Expand(""));
    }

    [Fact]
    public void NullText_Throws()
    {
        // 迭代器参数检查立即执行（公开方法非迭代器 + 私有链拆分）
        Assert.Throws<ArgumentNullException>(() => IssueReferenceExpander.Expand(null!));
    }
}
