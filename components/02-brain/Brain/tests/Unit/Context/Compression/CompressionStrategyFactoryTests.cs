
namespace Core.Tests.Context.Compression;

public class CompressionStrategyFactoryTests
{
    private readonly CompressionStrategyFactory _factory = new();

    [Fact]
    public void Constructor_ShouldRegisterDefaultStrategies()
    {
        var strategies = _factory.GetAllStrategies().ToList();

        strategies.Should().Contain(s => s.Name == "CodeContentCompressor");
        strategies.Should().Contain(s => s.Name == "DialogueCompressor");
        strategies.Should().Contain(s => s.Name == "ReferenceIndexCompressor");
    }

    [Fact]
    public void HasStrategyFor_CodeType_ShouldReturnTrue()
    {
        _factory.HasStrategyFor(ContentType.Code).Should().BeTrue();
    }

    [Fact]
    public void HasStrategyFor_DialogueType_ShouldReturnTrue()
    {
        _factory.HasStrategyFor(ContentType.Dialogue).Should().BeTrue();
    }

    [Fact]
    public void HasStrategyFor_ReferenceIndexType_ShouldReturnTrue()
    {
        _factory.HasStrategyFor(ContentType.ReferenceIndex).Should().BeTrue();
    }

    [Fact]
    public void HasStrategyFor_TextType_ShouldReturnFalse()
    {
        _factory.HasStrategyFor(ContentType.Text).Should().BeFalse();
    }

    [Fact]
    public void GetStrategy_CodeContent_ShouldReturnCodeCompressor()
    {
        var code = "public class Test { public void Method() { var x = 1; Console.WriteLine(x); } }";
        var strategy = _factory.GetStrategy(code, ContentType.Code);

        strategy.Should().NotBeNull();
        strategy!.Name.Should().Be("CodeContentCompressor");
    }

    [Fact]
    public void GetStrategy_DialogueContent_ShouldReturnDialogueCompressor()
    {
        var dialogue = @"User: Hello, how are you today?
Assistant: Hi! I'm doing great, thank you for asking.
User: Can you help me with a coding problem?
Assistant: Of course! I'd be happy to help. What do you need assistance with?";
        var strategy = _factory.GetStrategy(dialogue, ContentType.Dialogue);

        strategy.Should().NotBeNull();
        strategy!.Name.Should().Be("DialogueCompressor");
    }

    [Fact]
    public void GetStrategy_ReferenceIndexContent_ShouldReturnReferenceCompressor()
    {
        var content = @"文件: Test.cs
class Test
method Method1

文件: Another.cs
class Another
method AnotherMethod";
        var strategy = _factory.GetStrategy(content, ContentType.ReferenceIndex);

        strategy.Should().NotBeNull();
        strategy!.Name.Should().Be("ReferenceIndexCompressor");
    }

    [Fact]
    public void GetStrategy_NoMatchingStrategy_ShouldReturnNull()
    {
        var content = "Some content";
        var strategy = _factory.GetStrategy(content, ContentType.Text);

        strategy.Should().BeNull();
    }

    [Fact]
    public void GetStrategiesForType_CodeType_ShouldReturnCodeCompressor()
    {
        var strategies = _factory.GetStrategiesForType(ContentType.Code).ToList();

        strategies.Should().Contain(s => s.Name == "CodeContentCompressor");
    }

    [Fact]
    public void GetStrategiesForType_ShouldReturnOrderedByPriority()
    {
        var strategies = _factory.GetStrategiesForType(ContentType.Code).ToList();

        for (int i = 1; i < strategies.Count; i++)
        {
            strategies[i - 1].Priority.Should().BeGreaterThanOrEqualTo(strategies[i].Priority);
        }
    }

    [Fact]
    public void RegisterStrategy_NewStrategy_ShouldSucceed()
    {
        var customStrategy = new TestCompressionStrategy();

        _factory.RegisterStrategy(customStrategy);

        _factory.HasStrategy("TestStrategy").Should().BeTrue();
    }

    [Fact]
    public void RegisterStrategy_DuplicateName_ShouldThrowException()
    {
        var customStrategy = new TestCompressionStrategy();
        _factory.RegisterStrategy(customStrategy);

        Action act = () => _factory.RegisterStrategy(customStrategy);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void UnregisterStrategy_ExistingStrategy_ShouldReturnTrue()
    {
        var customStrategy = new TestCompressionStrategy();
        _factory.RegisterStrategy(customStrategy);

        var result = _factory.UnregisterStrategy("TestStrategy");

        result.Should().BeTrue();
        _factory.HasStrategy("TestStrategy").Should().BeFalse();
    }

    [Fact]
    public void UnregisterStrategy_NonExistingStrategy_ShouldReturnFalse()
    {
        var result = _factory.UnregisterStrategy("NonExistingStrategy");

        result.Should().BeFalse();
    }

    [Fact]
    public void GetAllStrategies_ShouldReturnAllRegisteredStrategies()
    {
        var strategies = _factory.GetAllStrategies().ToList();

        strategies.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void RegisterStrategies_MultipleStrategies_ShouldRegisterAll()
    {
        var factory = new CompressionStrategyFactory();
        var strategies = new[]
        {
            new TestCompressionStrategy("Strategy1"),
            new TestCompressionStrategy("Strategy2")
        };

        factory.RegisterStrategies(strategies);

        factory.HasStrategy("Strategy1").Should().BeTrue();
        factory.HasStrategy("Strategy2").Should().BeTrue();
    }

    [Fact]
    public void GetBestStrategyForType_CodeType_ShouldReturnCodeCompressor()
    {
        var strategy = _factory.GetBestStrategyForType(ContentType.Code);

        strategy.Should().NotBeNull();
        strategy!.Name.Should().Be("CodeContentCompressor");
    }

    [Fact]
    public void GetBestStrategyForType_NoStrategy_ShouldReturnNull()
    {
        var strategy = _factory.GetBestStrategyForType(ContentType.Log);

        strategy.Should().BeNull();
    }

    [Fact]
    public void HasStrategy_ExistingStrategy_ShouldReturnTrue()
    {
        _factory.HasStrategy("CodeContentCompressor").Should().BeTrue();
    }

    [Fact]
    public void HasStrategy_NonExistingStrategy_ShouldReturnFalse()
    {
        _factory.HasStrategy("NonExistingStrategy").Should().BeFalse();
    }

    [Fact]
    public void HasStrategy_CaseInsensitive_ShouldReturnTrue()
    {
        _factory.HasStrategy("codecontentcompressor").Should().BeTrue();
        _factory.HasStrategy("CODECONTENTCOMPRESSOR").Should().BeTrue();
    }

    private class TestCompressionStrategy : ICompressionStrategy
    {
        private readonly string _name;

        public TestCompressionStrategy(string name = "TestStrategy")
        {
            _name = name;
        }

        public string Name => _name;
        public string Description => "Test compression strategy";
        public IReadOnlySet<ContentType> SupportedContentTypes { get; } = new HashSet<ContentType> { ContentType.Text };
        public int Priority => 50;

        public Task<string> CompressAsync(string content, CompressionOptions options, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(content);
        }

        public bool CanHandle(string content, ContentType contentType)
        {
            return SupportedContentTypes.Contains(contentType);
        }

        public double EstimateCompressionRatio(string content, CompressionOptions options)
        {
            return 0.5;
        }
    }
}
