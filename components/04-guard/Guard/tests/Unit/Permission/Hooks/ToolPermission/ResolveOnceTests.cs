
namespace Core.Tests.Hooks.ToolPermission;

public class ResolveOnceTests
{
    [Fact]
    public void Claim_FirstCall_ShouldReturnTrue()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });

        var result = resolveOnce.Claim();

        result.Should().BeTrue();
    }

    [Fact]
    public void Claim_SecondCall_ShouldReturnFalse()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });
        resolveOnce.Claim();

        var result = resolveOnce.Claim();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsResolved_InitialState_ShouldReturnFalse()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });

        var result = resolveOnce.IsResolved();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsResolved_AfterClaim_ShouldReturnTrue()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });
        resolveOnce.Claim();

        var result = resolveOnce.IsResolved();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsResolved_AfterResolve_ShouldReturnTrue()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });

        resolveOnce.Resolve("test");

        resolveOnce.IsResolved().Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldCallResolveAction()
    {
        string? resolvedValue = null;
        var resolveOnce = new ResolveOnce<string>(v => resolvedValue = v);

        resolveOnce.Resolve("test_value");

        resolvedValue.Should().Be("test_value");
    }

    [Fact]
    public void Resolve_MultipleCalls_ShouldOnlyResolveOnce()
    {
        var callCount = 0;
        var resolveOnce = new ResolveOnce<string>(_ => callCount++);

        resolveOnce.Resolve("first");
        resolveOnce.Resolve("second");
        resolveOnce.Resolve("third");

        callCount.Should().Be(1);
    }

    [Fact]
    public void Resolve_AfterClaim_ShouldStillWork()
    {
        string? resolvedValue = null;
        var resolveOnce = new ResolveOnce<string>(v => resolvedValue = v);

        resolveOnce.Claim();
        resolveOnce.Resolve("test");

        resolvedValue.Should().Be("test");
    }

    [Fact]
    public void Claim_ThenResolve_ShouldBeResolved()
    {
        var resolveOnce = new ResolveOnce<string>(_ => { });

        resolveOnce.Claim();
        resolveOnce.Resolve("test");

        resolveOnce.IsResolved().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Resolve_IntValue_ShouldWork(int value)
    {
        int? resolvedValue = null;
        var resolveOnce = new ResolveOnce<int>(v => resolvedValue = v);

        resolveOnce.Resolve(value);

        resolvedValue.Should().Be(value);
    }

    [Fact]
    public void Resolve_NullValue_ShouldWork()
    {
        string? resolvedValue = "initial";
        var resolveOnce = new ResolveOnce<string?>(v => resolvedValue = v);

        resolveOnce.Resolve(null);

        resolvedValue.Should().BeNull();
    }

    [Fact]
    public void Resolve_ComplexObject_ShouldWork()
    {
        TestObject? resolvedObject = null;
        var expectedObject = new TestObject { Id = 1, Name = "Test" };
        var resolveOnce = new ResolveOnce<TestObject>(v => resolvedObject = v);

        resolveOnce.Resolve(expectedObject);

        resolvedObject.Should().BeEquivalentTo(expectedObject);
    }

    [Fact]
    public async Task ConcurrentClaims_OnlyOneShouldSucceed()
    {
        var resolveOnce = new ResolveOnce<int>(_ => { });
        var successfulClaims = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                if (resolveOnce.Claim())
                {
                    Interlocked.Increment(ref successfulClaims);
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);

        successfulClaims.Should().Be(1);
    }

    private class TestObject
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
