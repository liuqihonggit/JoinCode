namespace Core.Utils;

public class BackpressureChannelCalculateCriticalDelayTests {
    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(101, 100, 100)]
    [InlineData(102, 100, 200)]
    [InlineData(105, 100, 500)]
    [InlineData(110, 100, 1000)]
    [InlineData(120, 100, 1000)]
    [InlineData(200, 100, 1000)]
    public void CalculateCriticalDelay_overflow_formula(int count, int high, int expectedMs) {
        BackpressureChannel<object>.CalculateCriticalDelay(count, high)
            .Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void CalculateCriticalDelay_zero_overflow_returns_zero() {
        BackpressureChannel<object>.CalculateCriticalDelay(50, 50)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateCriticalDelay_caps_at_one_second() {
        BackpressureChannel<object>.CalculateCriticalDelay(1000, 0)
            .Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalculateCriticalDelay_large_overflow_still_capped() {
        BackpressureChannel<object>.CalculateCriticalDelay(int.MaxValue, 0)
            .Should().Be(TimeSpan.FromSeconds(1));
    }
}
