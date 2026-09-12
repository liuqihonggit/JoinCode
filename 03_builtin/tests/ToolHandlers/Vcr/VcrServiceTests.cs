// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Core.Tests.Services.Vcr;

public sealed class VcrServiceTests : IDisposable
{
    private readonly VcrService _service;
    private readonly VcrOptions _options;
    private readonly string _tempDir;

    public VcrServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vcr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _options = new VcrOptions
        {
            Mode = VcrMode.None,
            CassettesDirectory = _tempDir
        };
        _service = new VcrService(_options, new IO.FileSystem.PhysicalFileSystem());
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (TestFileSystem.Current.DirectoryExists(_tempDir))
            {
                TestFileSystem.Current.DeleteDirectory(_tempDir, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to cleanup temp directory '{_tempDir}': {ex.Message}");
        }
    }

    [Fact]
    public void SetMode_Record_SetsModeToRecord()
    {
        _service.SetMode(VcrMode.Record);

        _service.CurrentMode.Should().Be(VcrMode.Record);
    }

    [Fact]
    public void SetMode_Playback_SetsModeToPlayback()
    {
        _service.SetMode(VcrMode.Playback);

        _service.CurrentMode.Should().Be(VcrMode.Playback);
    }

    [Fact]
    public void SetMode_None_SetsModeToNone()
    {
        _service.SetMode(VcrMode.Record);
        _service.SetMode(VcrMode.None);

        _service.CurrentMode.Should().Be(VcrMode.None);
    }

    [Fact]
    public async Task LoadCassetteAsync_NewCassette_CreatesEmptyCassette()
    {
        var cassette = await _service.LoadCassetteAsync("test-cassette").ConfigureAwait(true);

        cassette.Should().NotBeNull();
        cassette.Name.Should().Be("test-cassette");
    }

    [Fact]
    public async Task LoadCassetteAsync_SameName_ReturnsCachedCassette()
    {
        var first = await _service.LoadCassetteAsync("cache-test").ConfigureAwait(true);
        var second = await _service.LoadCassetteAsync("cache-test").ConfigureAwait(true);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task RecordInteractionAsync_WhenNotRecordMode_DoesNotRecord()
    {
        _service.SetMode(VcrMode.None);
        var cassette = await _service.LoadCassetteAsync("no-record").ConfigureAwait(true);
        var initialCount = cassette.Interactions.Count;

        await _service.RecordInteractionAsync("no-record", new VcrRequest { Method = "GET", Uri = "/test" }, new VcrResponse { Status = 200 }).ConfigureAwait(true);

        cassette.Interactions.Should().HaveCount(initialCount);
    }

    [Fact]
    public async Task RecordInteractionAsync_WhenRecordMode_RecordsInteraction()
    {
        _service.SetMode(VcrMode.Record);

        await _service.RecordInteractionAsync("rec-test", new VcrRequest { Method = "GET", Uri = "/api/test" }, new VcrResponse { Status = 200, StatusText = "OK" }).ConfigureAwait(true);

        var cassette = await _service.LoadCassetteAsync("rec-test").ConfigureAwait(true);
        cassette.Interactions.Should().NotBeEmpty();
        cassette.Interactions[0].Request.Method.Should().Be("GET");
        cassette.Interactions[0].Response.Status.Should().Be(200);
    }

    [Fact]
    public async Task FindMatchingInteractionAsync_WhenNotPlaybackMode_ReturnsNull()
    {
        _service.SetMode(VcrMode.Record);

        var result = await _service.FindMatchingInteractionAsync("test", new VcrRequest { Method = "GET", Uri = "/test" }).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchingInteractionAsync_WhenPlaybackModeWithMatch_ReturnsResponse()
    {
        _service.SetMode(VcrMode.Record);
        await _service.RecordInteractionAsync("playback-test", new VcrRequest { Method = "GET", Uri = "/api/data" }, new VcrResponse { Status = 200, StatusText = "OK" }).ConfigureAwait(true);

        _service.SetMode(VcrMode.Playback);
        var result = await _service.FindMatchingInteractionAsync("playback-test", new VcrRequest { Method = "GET", Uri = "/api/data" }).ConfigureAwait(true);

        result.Should().NotBeNull();
        result!.Status.Should().Be(200);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var act = () => new VcrService(null!, new IO.FileSystem.PhysicalFileSystem());

        act.Should().Throw<ArgumentNullException>();
    }
}
