namespace Infra.Tests.Utils.Collections;

public class CachedRegistryTests
{
    [Fact]
    public void Register_And_TryGetValue_ShouldWork()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Register("a", 1);
        registry.TryGetValue("a", out var value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void TryGetValue_MissingKey_ShouldReturnFalse()
    {
        var registry = new CachedRegistry<string, int>();
        registry.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void RegisterAlias_ShouldMapToValue()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Register("a", 1);
        registry.RegisterAlias("alias_a", 1);
        registry.TryGetValue("alias_a", out var value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void Unregister_ShouldRemoveKey()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Register("a", 1);
        registry.Unregister("a").Should().BeTrue();
        registry.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Unregister_MissingKey_ShouldReturnFalse()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Unregister("a").Should().BeFalse();
    }

    [Fact]
    public void Count_ShouldReturnCorrectCount()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Count.Should().Be(0);
        registry.Register("a", 1);
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void ContainsKey_ShouldWork()
    {
        var registry = new CachedRegistry<string, int>();
        registry.Register("a", 1);
        registry.ContainsKey("a").Should().BeTrue();
        registry.ContainsKey("b").Should().BeFalse();
    }
}

public class CategorizedRegistryTests
{
    [Fact]
    public void TryGetValue_ShouldWork()
    {
        var registry = new CategorizedRegistry<string, int, string>("default");
        registry.Register("a", 1);
        registry.TryGetValue("a", out var value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void TryGetValue_DisabledItem_ShouldReturnFalse()
    {
        var registry = new CategorizedRegistry<string, int, string>("default", isEnabled: v => v > 0);
        registry.Register("a", -1);
        registry.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void SetCategory_ShouldAssignCategory()
    {
        var registry = new CategorizedRegistry<string, int, string>("default");
        registry.Register("a", 1);
        registry.SetCategory("a", "cat1");
        var entries = registry.GetCategorizedEntries();
        entries.Should().ContainSingle(e => e.Key == "a" && e.Category == "cat1");
    }

    [Fact]
    public void GetCategorizedEntries_DefaultCategory_ShouldBeUsed()
    {
        var registry = new CategorizedRegistry<string, int, string>("uncategorized");
        registry.Register("a", 1);
        var entries = registry.GetCategorizedEntries();
        entries.Should().ContainSingle(e => e.Key == "a" && e.Category == "uncategorized");
    }

    [Fact]
    public void Unregister_ShouldRemoveKey()
    {
        var registry = new CategorizedRegistry<string, int, string>("default");
        registry.Register("a", 1);
        registry.Unregister("a").Should().BeTrue();
        registry.TryGetValue("a", out _).Should().BeFalse();
    }
}
