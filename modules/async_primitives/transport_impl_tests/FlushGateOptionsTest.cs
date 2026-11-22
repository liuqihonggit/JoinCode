namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// FlushGateOptions 守卫确定性测试 — MaxBatchSize/FlushIntervalMs/MaxWaitMs 范围(TASK031)
/// <para>确定性:不依赖时序/IO,给定非法值 → 断言抛 ArgumentOutOfRangeException</para>
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class FlushGateOptionsTest {
    [Fact]
    public void MaxBatchSize_Zero_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { MaxBatchSize = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxBatchSize");
    }

    [Fact]
    public void MaxBatchSize_Negative_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { MaxBatchSize = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxBatchSize");
    }

    [Fact]
    public void FlushIntervalMs_Zero_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { FlushIntervalMs = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("FlushIntervalMs");
    }

    [Fact]
    public void FlushIntervalMs_Negative_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { FlushIntervalMs = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("FlushIntervalMs");
    }

    [Fact]
    public void MaxWaitMs_Zero_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { MaxWaitMs = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxWaitMs");
    }

    [Fact]
    public void MaxWaitMs_Negative_ThrowsArgumentOutOfRangeException() {
        Action act = () => new FlushGateOptions { MaxWaitMs = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxWaitMs");
    }

    [Fact]
    public void DefaultValues_AreValid() {
        var opts = FlushGateOptions.CreateDefault();
        opts.MaxBatchSize.Should().BeGreaterThan(0);
        opts.FlushIntervalMs.Should().BeGreaterThan(0);
        opts.MaxWaitMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ValidValues_ConstructSuccessfully() {
        var opts = new FlushGateOptions { MaxBatchSize = 50, FlushIntervalMs = 500, MaxWaitMs = 3000 };
        opts.MaxBatchSize.Should().Be(50);
        opts.FlushIntervalMs.Should().Be(500);
        opts.MaxWaitMs.Should().Be(3000);
    }
}
