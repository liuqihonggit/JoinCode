namespace Abs.Tests.Utils;

/// <summary>
/// BotNameGenerator 单元测试 — 验证 bot 前缀、中文名、去重与释放行为
/// </summary>
public sealed class BotNameGeneratorTests
{
    [Fact]
    public void Generate_ReturnsBotPrefix()
    {
        BotNameGenerator.Clear();
        var name = BotNameGenerator.Generate();
        name.Should().StartWith("bot");
        name.Length.Should().BeGreaterThan(3);
    }

    [Fact]
    public void Generate_ReturnsChineseName()
    {
        BotNameGenerator.Clear();
        var name = BotNameGenerator.Generate();
        var chinesePart = name[3..];
        chinesePart.Should().NotBeEmpty();
        chinesePart.Should().MatchRegex(@"^[\u4e00-\u9fff]+$");
    }

    [Fact]
    public void Generate_MultipleCalls_NoDuplicate()
    {
        BotNameGenerator.Clear();
        var names = new HashSet<string>();
        for (var i = 0; i < 36; i++)
            names.Add(BotNameGenerator.Generate());
        names.Should().HaveCount(36);
    }

    [Fact]
    public void Generate_ExceedingPoolSize_ReturnsFallbackWithNumber()
    {
        BotNameGenerator.Clear();
        var names = new HashSet<string>();
        for (var i = 0; i < 40; i++)
            names.Add(BotNameGenerator.Generate());
        names.Should().HaveCount(40);
        names.Should().Contain(n => n.StartsWith("bot") && n.Length > 3 && IsNumeric(n.Substring(3)));
    }

    [Fact]
    public void Release_AllowsReuse()
    {
        BotNameGenerator.Clear();
        var name = BotNameGenerator.Generate();
        BotNameGenerator.Release(name);
        BotNameGenerator.Generate();
    }

    [Fact]
    public void Clear_RemovesAllNames()
    {
        BotNameGenerator.Generate();
        BotNameGenerator.Generate();
        BotNameGenerator.Clear();
        var name = BotNameGenerator.Generate();
        name.Should().StartWith("bot");
    }

    private static bool IsNumeric(string s) => int.TryParse(s, out _);
}
