
namespace Core.Tests.Prompts;

public class SynonymAnalyzerTests
{
    [Fact]
    public void Analyze_EmptyInput_ReturnsEmptyList()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["test"] = "supplementary"
        });

        var result = SynonymAnalyzer.Analyze("", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_NullInput_ReturnsEmptyList()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["test"] = "supplementary"
        });

        var result = SynonymAnalyzer.Analyze(null!, map);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_EmptyMap_ReturnsEmptyList()
    {
        var map = new SynonymMap();

        var result = SynonymAnalyzer.Analyze("hello world", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MatchingKey_ReturnsMatch()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["deploy"] = "When the user says deploy, they mean deploying the application to the target environment. Please ask for the target environment (staging/production) before proceeding."
        });

        var result = SynonymAnalyzer.Analyze("please deploy the app", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("deploy");
        result[0].HasMatch.Should().BeTrue();
    }

    [Fact]
    public void Analyze_CaseInsensitiveMatch_ReturnsMatch()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["Deploy"] = "supplementary content"
        });

        var result = SynonymAnalyzer.Analyze("please DEPLOY the app", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("Deploy");
    }

    [Fact]
    public void Analyze_NoMatch_ReturnsEmptyList()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["deploy"] = "supplementary content"
        });

        var result = SynonymAnalyzer.Analyze("please build the app", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MultipleMatches_ReturnsAllMatches()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["deploy"] = "deployment supplementary",
            ["build"] = "build supplementary"
        });

        var result = SynonymAnalyzer.Analyze("please build and deploy the app", map);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Analyze_PartialMatch_ReturnsMatch()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["git"] = "git operations supplementary"
        });

        var result = SynonymAnalyzer.Analyze("I need to do a git push", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("git");
    }

    [Fact]
    public void Analyze_SupplementaryContent_PreservedCorrectly()
    {
        var expectedContent = "When the user mentions k8s, they mean Kubernetes. Please use Kubernetes terminology and provide Kubernetes-specific guidance.";
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["k8s"] = expectedContent
        });

        var result = SynonymAnalyzer.Analyze("deploy to k8s", map);

        result.Should().HaveCount(1);
        result[0].SupplementaryContent.Should().Be(expectedContent);
    }
}

public class SynonymMapTests
{
    [Fact]
    public void DefaultConstructor_CreatesEmptyMap()
    {
        var map = new SynonymMap();

        map.Entries.Should().BeEmpty();
        map.ContainsKey("anything").Should().BeFalse();
    }

    [Fact]
    public void CustomMap_CreatesFromDictionary()
    {
        var dict = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };

        var map = new SynonymMap(dict);

        map.Entries.Should().HaveCount(2);
        map.ContainsKey("key1").Should().BeTrue();
        map.ContainsKey("key2").Should().BeTrue();
    }

    [Fact]
    public void TryGetValue_CaseInsensitive()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["Deploy"] = "deployment content"
        });

        map.TryGetValue("deploy", out var value).Should().BeTrue();
        value.Should().Be("deployment content");

        map.TryGetValue("DEPLOY", out var value2).Should().BeTrue();
        value2.Should().Be("deployment content");
    }

    [Fact]
    public void TryGetValue_KeyNotFound_ReturnsFalse()
    {
        var map = new SynonymMap(new Dictionary<string, string>
        {
            ["deploy"] = "content"
        });

        map.TryGetValue("build", out _).Should().BeFalse();
    }
}
