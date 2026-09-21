
namespace Core.Tests.Prompts;

public class SynonymAnalyzerTests {
    [Fact]
    public async Task Analyze_EmptyInput_ReturnsEmptyList() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["test"] = "supplementary"
        });

        var result = SynonymAnalyzer.Analyze("", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Analyze_NullInput_ReturnsEmptyList() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["test"] = "supplementary"
        });

        var result = SynonymAnalyzer.Analyze(null!, map);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Analyze_EmptyMap_ReturnsEmptyList() {
        await using var map = new SynonymMap();

        var result = SynonymAnalyzer.Analyze("hello world", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Analyze_MatchingKey_ReturnsMatch() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["deploy"] = "When the user says deploy, they mean deploying the application to the target environment. Please ask for the target environment (staging/production) before proceeding."
        });

        var result = SynonymAnalyzer.Analyze("please deploy the app", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("deploy");
        result[0].HasMatch.Should().BeTrue();
    }

    [Fact]
    public async Task Analyze_CaseInsensitiveMatch_ReturnsMatch() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["Deploy"] = "supplementary content"
        });

        var result = SynonymAnalyzer.Analyze("please DEPLOY the app", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("Deploy");
    }

    [Fact]
    public async Task Analyze_NoMatch_ReturnsEmptyList() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["deploy"] = "supplementary content"
        });

        var result = SynonymAnalyzer.Analyze("please build the app", map);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Analyze_MultipleMatches_ReturnsAllMatches() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["deploy"] = "deployment supplementary",
            ["build"] = "build supplementary"
        });

        var result = SynonymAnalyzer.Analyze("please build and deploy the app", map);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Analyze_PartialMatch_ReturnsMatch() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["git"] = "git operations supplementary"
        });

        var result = SynonymAnalyzer.Analyze("I need to do a git push", map);

        result.Should().HaveCount(1);
        result[0].MatchedKey.Should().Be("git");
    }

    [Fact]
    public async Task Analyze_SupplementaryContent_PreservedCorrectly() {
        var expectedContent = "When the user mentions k8s, they mean Kubernetes. Please use Kubernetes terminology and provide Kubernetes-specific guidance.";
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["k8s"] = expectedContent
        });

        var result = SynonymAnalyzer.Analyze("deploy to k8s", map);

        result.Should().HaveCount(1);
        result[0].SupplementaryContent.Should().Be(expectedContent);
    }
}

public class SynonymMapTests {
    [Fact]
    public async Task DefaultConstructor_CreatesEmptyMap() {
        await using var map = new SynonymMap();

        map.Entries.Should().BeEmpty();
        map.ContainsKey("anything").Should().BeFalse();
    }

    [Fact]
    public async Task CustomMap_CreatesFromDictionary() {
        var dict = new Dictionary<string, string> {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };
        await using var map = new SynonymMap(dict);

        map.Entries.Should().HaveCount(2);
        map.ContainsKey("key1").Should().BeTrue();
        map.ContainsKey("key2").Should().BeTrue();
    }

    [Fact]
    public async Task TryGetValue_CaseInsensitive() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["Deploy"] = "deployment content"
        });

        map.TryGetValue("deploy", out var value).Should().BeTrue();
        value.Should().Be("deployment content");

        map.TryGetValue("DEPLOY", out var value2).Should().BeTrue();
        value2.Should().Be("deployment content");
    }

    [Fact]
    public async Task TryGetValue_KeyNotFound_ReturnsFalse() {
        await using var map = new SynonymMap(new Dictionary<string, string> {
            ["deploy"] = "content"
        });

        map.TryGetValue("build", out _).Should().BeFalse();
    }
}