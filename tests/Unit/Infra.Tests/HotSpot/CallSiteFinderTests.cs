namespace Infra.Tests.HotSpot;


public sealed class CallSiteFinderTests
{
    private static CodeCallSite MakeCallSite(string file, int line) =>
        new() { FilePath = file, LineNumber = line, LineContent = "IFoo foo = new();", MatchType = "reference" };

    [Fact]
    public async Task FindCallSites_ShouldReturnResultsFromSearchFunc()
    {
        var sites = new List<CodeCallSite>
        {
            MakeCallSite("src/A.cs", 10),
            MakeCallSite("src/B.cs", 25),
            MakeCallSite("src/C.cs", 5)
        };
        ICallSiteFinder sut = new CallSiteFinder((_, _, _) => Task.FromResult<IReadOnlyList<CodeCallSite>>(sites));

        var result = await sut.FindCallSitesAsync("IFoo", "src/");

        result.Should().HaveCount(3);
        result.Select(s => s.FilePath).Should().BeEquivalentTo(["src/A.cs", "src/B.cs", "src/C.cs"]);
    }

    [Fact]
    public async Task FindCallSites_NoMatches_ShouldReturnEmpty()
    {
        ICallSiteFinder sut = new CallSiteFinder((_, _, _) => Task.FromResult<IReadOnlyList<CodeCallSite>>([]));

        var result = await sut.FindCallSitesAsync("NonExistent", "src/");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindCallSites_EmptySymbol_ShouldThrow()
    {
        ICallSiteFinder sut = new CallSiteFinder((_, _, _) => Task.FromResult<IReadOnlyList<CodeCallSite>>([]));

        var act = () => sut.FindCallSitesAsync("", "src/");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
