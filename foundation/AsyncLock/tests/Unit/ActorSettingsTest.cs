namespace Core.Utils;

/// <summary>
/// ActorSettings 单元测试 — 验证默认值、校验合法性、友好提示词。
/// </summary>
public class ActorSettingsTest
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new ActorSettings();

        settings.BuildQueue.Mode.Should().Be("serial");
        settings.BuildQueue.WorkerCount.Should().Be(2);
        settings.BuildQueue.IsParallel.Should().BeFalse();

        settings.Backpressure.CodingAgentTask.Capacity.Should().Be(2000);
        settings.Backpressure.LlmGateway.Capacity.Should().Be(200);
        settings.Backpressure.Router.Capacity.Should().Be(1000);
        settings.Backpressure.Build.Capacity.Should().Be(100);
    }

    [Fact]
    public void Validate_DefaultValues_Passes()
    {
        var settings = new ActorSettings();
        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void BuildQueue_IsParallel_TrueWhenModeIsParallel()
    {
        var settings = new ActorSettings { BuildQueue = { Mode = "parallel" } };
        settings.BuildQueue.IsParallel.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidMode_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings { BuildQueue = { Mode = "invalid" } };
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*必须为 'serial' 或 'parallel'*settings.json*");
    }

    [Fact]
    public void Validate_ZeroWorkerCount_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings { BuildQueue = { WorkerCount = 0 } };
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*WorkerCount 必须 >= 1*settings.json*");
    }

    [Fact]
    public void Validate_OverMaxWorkerCount_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings { BuildQueue = { WorkerCount = 20 } };
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*WorkerCount 建议 <= 16*settings.json*");
    }

    [Fact]
    public void Validate_NegativeCapacity_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings();
        settings.Backpressure.Build.Capacity = -1;
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Capacity 必须 >= 0*settings.json*");
    }

    [Fact]
    public void Validate_HighWatermarkOverCapacity_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings();
        settings.Backpressure.Build.Capacity = 100;
        settings.Backpressure.Build.HighWatermark = 200;
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*HighWatermark*不能超过 Capacity*settings.json*");
    }

    [Fact]
    public void Validate_NegativeSendTimeout_ThrowsWithFriendlyMessage()
    {
        var settings = new ActorSettings();
        settings.Backpressure.Build.SendTimeoutSeconds = -1;
        var act = () => settings.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SendTimeoutSeconds 必须 > 0*settings.json*");
    }

    [Fact]
    public void Validate_CustomValidConfig_Passes()
    {
        var settings = new ActorSettings
        {
            BuildQueue = { Mode = "parallel", WorkerCount = 4 },
            Backpressure =
            {
                CodingAgentTask = new BackpressurePreset(3000, 45),
                LlmGateway = new BackpressurePreset(300, 90),
                Router = new BackpressurePreset(1500, 15),
                Build = new BackpressurePreset(150, 120)
            }
        };
        var act = () => settings.Validate();
        act.Should().NotThrow();
    }
}
