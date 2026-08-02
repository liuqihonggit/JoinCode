namespace Hands.Tests.Api.Vcr;

public sealed class VcrCassetteTests
{
    [Fact]
    public void DefaultValues_ShouldBeInitialized()
    {
        var cassette = new global::Services.Api.Vcr.VcrCassette();

        cassette.Name.Should().BeEmpty();
        cassette.Interactions.Should().BeEmpty();
    }

    [Fact]
    public void Interactions_ShouldBeMutable()
    {
        var cassette = new global::Services.Api.Vcr.VcrCassette
        {
            Name = "test",
            Interactions =
            [
                new global::Services.Api.Vcr.VcrInteraction
                {
                    Request = new global::Services.Api.Vcr.VcrRequest { Method = "GET", Uri = "https://example.com" },
                    Response = new global::Services.Api.Vcr.VcrResponse { Status = 200 }
                }
            ]
        };

        cassette.Interactions.Should().ContainSingle();
        cassette.Interactions[0].Request.Method.Should().Be("GET");
        cassette.Interactions[0].Response.Status.Should().Be(200);
    }
}
