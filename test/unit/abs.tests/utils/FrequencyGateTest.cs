namespace Abs.Tests.Utils;

/// <summary>
/// FrequencyGate 单元测试 — 验证高频闹钟/低频冷却对称逻辑。
/// </summary>
public sealed class FrequencyGateTest {
    private static string NewKey() => $"gate-key-{Guid.NewGuid():N}";

    [Fact]
    public void HighFrequency_ShouldSignal_WhenCountReachesThreshold() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Threshold = 3,
            Direction = GateDirection.HighFrequency,
        };

        gate.Record(key);
        gate.Record(key);
        gate.ShouldSignal(key, config).Should().BeFalse();

        gate.Record(key);
        gate.ShouldSignal(key, config).Should().BeTrue();
    }

    [Fact]
    public async Task HighFrequency_ShouldNotSignal_WhenWindowExpired() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromMilliseconds(50),
            Threshold = 2,
            Direction = GateDirection.HighFrequency,
        };

        gate.Record(key, config.Window);
        gate.Record(key, config.Window);
        gate.ShouldSignal(key, config).Should().BeTrue();

        await Task.Delay(80);
        gate.ShouldSignal(key, config).Should().BeFalse();
    }

    [Fact]
    public void LowFrequency_ShouldSignal_WhenNoEventsRecorded() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromMilliseconds(50),
            Direction = GateDirection.LowFrequency,
        };

        gate.ShouldSignal(key, config).Should().BeTrue();
    }

    [Fact]
    public void LowFrequency_ShouldNotSignal_WithinIntervalAfterRecord() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Direction = GateDirection.LowFrequency,
        };

        gate.Record(key);
        gate.ShouldSignal(key, config).Should().BeFalse();
    }

    [Fact]
    public async Task LowFrequency_ShouldSignal_AfterIntervalElapsed() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromMilliseconds(50),
            Direction = GateDirection.LowFrequency,
        };

        gate.Record(key);
        gate.ShouldSignal(key, config).Should().BeFalse();

        await Task.Delay(80);
        gate.ShouldSignal(key, config).Should().BeTrue();
    }

    [Fact]
    public void CountInWindow_ReturnsZero_WhenKeyNotFound() {
        var gate = new FrequencyGate();
        gate.CountInWindow("nonexistent", TimeSpan.FromSeconds(10)).Should().Be(0);
    }

    [Fact]
    public void TimeSinceLast_ReturnsMaxValue_WhenKeyNotFound() {
        var gate = new FrequencyGate();
        gate.TimeSinceLast("nonexistent").Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void GetTriggeredKeys_HighFrequency_ReturnsOnlyTriggeredKeys() {
        var gate = new FrequencyGate();
        var key1 = NewKey();
        var key2 = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Threshold = 2,
            Direction = GateDirection.HighFrequency,
        };

        gate.Record(key1);
        gate.Record(key1);
        gate.Record(key2);

        var triggered = gate.GetTriggeredKeys(config);
        triggered.Should().Contain(key1);
        triggered.Should().NotContain(key2);
    }

    [Fact]
    public async Task GetTriggeredKeys_LowFrequency_ReturnsKeysWithExpiredInterval() {
        var gate = new FrequencyGate();
        var key1 = NewKey();
        var key2 = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromMilliseconds(50),
            Direction = GateDirection.LowFrequency,
        };

        gate.Record(key1);
        gate.Record(key2);

        await Task.Delay(80);

        var triggered = gate.GetTriggeredKeys(config);
        triggered.Should().Contain(key1);
        triggered.Should().Contain(key2);
    }

    [Fact]
    public void Reset_ClearsAllEvents() {
        var gate = new FrequencyGate();
        var key = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Threshold = 1,
            Direction = GateDirection.HighFrequency,
        };

        gate.Record(key);
        gate.ShouldSignal(key, config).Should().BeTrue();

        gate.Reset();
        gate.ShouldSignal(key, config).Should().BeFalse();
    }

    [Fact]
    public void MultipleKeys_TrackedIndependently() {
        var gate = new FrequencyGate();
        var key1 = NewKey();
        var key2 = NewKey();
        var config = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Threshold = 2,
            Direction = GateDirection.HighFrequency,
        };

        gate.Record(key1);
        gate.Record(key1);
        gate.Record(key2);

        gate.ShouldSignal(key1, config).Should().BeTrue();
        gate.ShouldSignal(key2, config).Should().BeFalse();
    }

    [Fact]
    public void HighAndLowDirection_AreSymmetric() {
        var gate = new FrequencyGate();
        var key = NewKey();

        var highConfig = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Threshold = 3,
            Direction = GateDirection.HighFrequency,
        };
        var lowConfig = new GateConfig {
            Window = TimeSpan.FromSeconds(10),
            Direction = GateDirection.LowFrequency,
        };

        gate.ShouldSignal(key, highConfig).Should().BeFalse();
        gate.ShouldSignal(key, lowConfig).Should().BeTrue();

        gate.Record(key);
        gate.Record(key);
        gate.Record(key);

        gate.ShouldSignal(key, highConfig).Should().BeTrue();
        gate.ShouldSignal(key, lowConfig).Should().BeFalse();
    }
}
