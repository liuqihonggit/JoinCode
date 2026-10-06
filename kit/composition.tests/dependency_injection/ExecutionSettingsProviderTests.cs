namespace Core.Tests.DependencyInjection;

/// <summary>
/// ExecutionSettingsProvider 确定性测试 — EffortLevel/ThinkingEnabled 双变量模式 setter/getter。
/// <para>LoadPersistedEffort/LoadPersistedThinkingEnabled 依赖 SyncFileReader+ConfigLoader(IO)，跳过直接测试。</para>
/// </summary>
public sealed class ExecutionSettingsProviderTests {
    private WorkflowConfig _config;
    private readonly Mock<IFileSystem> _fs;
    private readonly Mock<IProviderDefinitionRegistry> _registry;

    public ExecutionSettingsProviderTests() {
        _config = new WorkflowConfig();
        _fs = new Mock<IFileSystem>();
        _registry = new Mock<IProviderDefinitionRegistry>();
    }

    private ExecutionSettingsProvider CreateSut(Mock<ITelemetryService>? telemetry = null) =>
        new(_config, _fs.Object, _registry.Object, telemetry?.Object);

    // ===== EffortLevel 双变量模式 =====

    [Theory]
    [InlineData(EffortLevel.Low)]
    [InlineData(EffortLevel.Medium)]
    [InlineData(EffortLevel.High)]
    [InlineData(EffortLevel.Max)]
    [InlineData(EffortLevel.Auto)]
    public async Task EffortLevel_SetThenGet_ReturnsSetValue(EffortLevel level) {
        await using var sut = CreateSut();
        sut.EffortLevel = level;
        sut.EffortLevel.Should().Be(level);
    }

    [Fact]
    public async Task EffortLevel_SetMultipleTimes_LastValueWins() {
        await using var sut = CreateSut();
        sut.EffortLevel = EffortLevel.Low;
        sut.EffortLevel = EffortLevel.High;
        sut.EffortLevel = EffortLevel.Medium;
        sut.EffortLevel.Should().Be(EffortLevel.Medium);
    }

    [Fact]
    public async Task EffortLevel_SetDifferentValue_RecordsTelemetry() {
        var mockCounter = new Mock<ITelemetryCounter>();
        var mockTelemetry = new Mock<ITelemetryService>();
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockCounter.Object);
        await using var sut = CreateSut(mockTelemetry);

        sut.EffortLevel = EffortLevel.High;

        mockCounter.Verify(c => c.Add(1.0, It.IsAny<Dictionary<string, string>?>()), Times.Once);
    }

    [Fact]
    public async Task EffortLevel_SetSameValue_DoesNotRecordTelemetry() {
        var mockCounter = new Mock<ITelemetryCounter>();
        var mockTelemetry = new Mock<ITelemetryService>();
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockCounter.Object);
        await using var sut = CreateSut(mockTelemetry);

        sut.EffortLevel = EffortLevel.Auto;
        sut.EffortLevel = EffortLevel.Auto;

        mockCounter.Verify(c => c.Add(1.0, It.IsAny<Dictionary<string, string>?>()), Times.Never);
    }

    [Fact]
    public async Task EffortLevel_SetDifferentValues_RecordsTelemetryForEachChange() {
        var mockCounter = new Mock<ITelemetryCounter>();
        var mockTelemetry = new Mock<ITelemetryService>();
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockCounter.Object);
        await using var sut = CreateSut(mockTelemetry);

        sut.EffortLevel = EffortLevel.Low;
        sut.EffortLevel = EffortLevel.Medium;
        sut.EffortLevel = EffortLevel.High;

        mockCounter.Verify(c => c.Add(1.0, It.IsAny<Dictionary<string, string>?>()), Times.Exactly(3));
    }

    [Fact]
    public async Task EffortLevel_SetWithNullTelemetry_DoesNotThrow() {
        await using var sut = CreateSut(telemetry: null);
        var act = () => sut.EffortLevel = EffortLevel.High;
        act.Should().NotThrow();
    }

    // ===== ThinkingEnabled 双变量模式 =====

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThinkingEnabled_SetThenGet_ReturnsSetValue(bool value) {
        await using var sut = CreateSut();
        sut.ThinkingEnabled = value;
        sut.ThinkingEnabled.Should().Be(value);
    }

    [Fact]
    public async Task ThinkingEnabled_SetMultipleTimes_LastValueWins() {
        await using var sut = CreateSut();
        sut.ThinkingEnabled = true;
        sut.ThinkingEnabled = false;
        sut.ThinkingEnabled = true;
        sut.ThinkingEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ThinkingEnabled_SetDoesNotRecordTelemetry() {
        var mockCounter = new Mock<ITelemetryCounter>();
        var mockTelemetry = new Mock<ITelemetryService>();
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockCounter.Object);
        await using var sut = CreateSut(mockTelemetry);

        sut.ThinkingEnabled = true;

        mockCounter.Verify(c => c.Add(It.IsAny<double>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
    }

    // ===== FastMode / FastModelId / Temperature / MaxTokens =====

    [Fact]
    public async Task FastMode_ReflectsConfigFastMode() {
        _config = _config with { FastMode = true };
        await using var sut = CreateSut();
        sut.FastMode.Should().BeTrue();
    }

    [Fact]
    public async Task FastMode_DefaultFalse() {
        await using var sut = CreateSut();
        sut.FastMode.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(null)]
    public async Task Temperature_SetThenGet_ReturnsSetValue(float? value) {
        await using var sut = CreateSut();
        sut.Temperature = value;
        sut.Temperature.Should().Be(value);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(null)]
    public async Task MaxTokens_SetThenGet_ReturnsSetValue(int? value) {
        await using var sut = CreateSut();
        sut.MaxTokens = value;
        sut.MaxTokens.Should().Be(value);
    }

    // LoadPersistedEffort / LoadPersistedThinkingEnabled 跳过：依赖 SyncFileReader.RunStringNullable
    // + ConfigLoader.LoadSettingFromSettingsJson（文件 IO），非确定性测试范畴。
    // 已改 internal 供未来 mock IFileSystem 测试，当前跳过。
}
