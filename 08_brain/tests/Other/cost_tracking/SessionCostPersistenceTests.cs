namespace Core.Tests.CostTracking;

public class SessionCostPersistenceTests
{
    private readonly Mock<IFileOperationService> _fileOpMock = new();
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "jcc-test-costs", Guid.NewGuid().ToString("N"));

    private Core.CostTracking.CostTracker CreateCostTracker()
    {
        return new Core.CostTracking.CostTracker(
            _fileOpMock.Object,
            storagePath: Path.Combine(_storagePath, "usage.json"),
            NullLogger<Core.CostTracking.CostTracker>.Instance);
    }

    private SessionCostPersistence CreatePersistence(Core.CostTracking.CostTracker tracker)
    {
        return new SessionCostPersistence(
            tracker,
            _fileOpMock.Object,
            NullLogger<SessionCostPersistence>.Instance);
    }

    [Fact]
    public async Task RestoreCostStateForSessionAsync_NoFile_ShouldReturnNull()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            var persistence = CreatePersistence(tracker);
            _fileOpMock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

            var result = await persistence.RestoreCostStateForSessionAsync("nonexistent-session").ConfigureAwait(true);

            result.Should().BeNull();
        }
    }

    [Fact]
    public async Task SaveCurrentSessionCostsAsync_ValidSession_ShouldWriteFile()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("gpt-4o", 100, 50, "test-session");

            var persistence = CreatePersistence(tracker);
            _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
            _fileOpMock.Setup(f => f.WriteFileAsync(It.Is<string>(p => p.Contains("costs")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileWriteResult.SuccessResult("path", "content", FileToolName.FileWrite.ToValue()));

            await persistence.SaveCurrentSessionCostsAsync("test-session").ConfigureAwait(true);

            _fileOpMock.Verify(
                f => f.WriteFileAsync(It.Is<string>(p => p.Contains("costs") && p.EndsWith("test-session.json")), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task SaveAndRestore_RoundTrip_ShouldPreserveData()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("gpt-4o", 100, 50, "session-rt");

            var persistence = CreatePersistence(tracker);

            var savedJson = string.Empty;
            _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
            _fileOpMock.Setup(f => f.WriteFileAsync(It.Is<string>(p => p.Contains("session-rt")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, content, _) => savedJson = content)
                .ReturnsAsync(FileWriteResult.SuccessResult("path", "content", FileToolName.FileWrite.ToValue()));
            _fileOpMock.Setup(f => f.WriteFileAsync(It.Is<string>(p => !p.Contains("session-rt")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileWriteResult.SuccessResult("path", "content", FileToolName.FileWrite.ToValue()));

            await persistence.SaveCurrentSessionCostsAsync("session-rt").ConfigureAwait(true);

            _fileOpMock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
            _fileOpMock.Setup(f => f.ReadFileAsync(It.Is<string>(p => p.Contains("session-rt")), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileReadResult.SuccessResult("path", savedJson, 1, 1, 1));

            var result = await persistence.RestoreCostStateForSessionAsync("session-rt").ConfigureAwait(true);

            result.Should().NotBeNull();
            result!.RequestCount.Should().Be(1);
            result.PromptTokens.Should().Be(100);
            result.CompletionTokens.Should().Be(50);
        }
    }

    [Fact]
    public async Task SaveCurrentSessionCostsAsync_EmptySessionId_ShouldThrowArgumentException()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            var persistence = CreatePersistence(tracker);
            var act = async () => await persistence.SaveCurrentSessionCostsAsync("").ConfigureAwait(true);

            await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task RestoreCostStateForSessionAsync_EmptySessionId_ShouldThrowArgumentException()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            var persistence = CreatePersistence(tracker);
            var act = async () => await persistence.RestoreCostStateForSessionAsync("  ").ConfigureAwait(true);

            await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task RestoreCostStateForSessionAsync_ReadFailure_ShouldReturnNull()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            var persistence = CreatePersistence(tracker);
            _fileOpMock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
            _fileOpMock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileReadResult.FailureResult("path", "read error"));

            var result = await persistence.RestoreCostStateForSessionAsync("session-fail").ConfigureAwait(true);

            result.Should().BeNull();
        }
    }

    [Fact]
    public async Task SaveCurrentSessionCostsAsync_WriteFailure_ShouldNotThrow()
    {
        var tracker = CreateCostTracker();
        await using (tracker)
        {
            tracker.RecordUsage("gpt-4o", 100, 50, "session-wf");

            var persistence = CreatePersistence(tracker);
            _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
            _fileOpMock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileWriteResult.FailureResult("path", "write error"));

            var act = async () => await persistence.SaveCurrentSessionCostsAsync("session-wf").ConfigureAwait(true);

            await act.Should().NotThrowAsync().ConfigureAwait(true);
        }
    }
}
