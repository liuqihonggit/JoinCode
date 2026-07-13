#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Configuration.Tests;

public sealed class FastModeServiceTests : IDisposable
{
    private static readonly string DefaultModelId = ModelConfigLoader.GetDefaultModelId("openai");
    private static readonly string DefaultFastModelId = ModelConfigLoader.GetDefaultFastModelId("openai");

    private readonly FastModeService _service;

    public FastModeServiceTests()
    {
        _service = new FastModeService(
            config: null,
            fastModelId: DefaultFastModelId,
            cooldownDuration: TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Initial_State_Should_Not_Be_Active()
    {
        Assert.False(_service.IsFastModeActive);
    }

    [Fact]
    public void Initial_PrimaryModel_Should_Be_Set()
    {
        Assert.Equal(DefaultModelId, _service.PrimaryModelId);
    }

    [Fact]
    public void Initial_FastModel_Should_Be_Set()
    {
        Assert.Equal(DefaultFastModelId, _service.FastModelId);
    }

    [Fact]
    public void Activate_Should_Set_IsActive()
    {
        _service.Activate();

        Assert.True(_service.IsFastModeActive);
    }

    [Fact]
    public void Activate_Should_Raise_Event()
    {
        FastModeChangedEventArgs? eventArgs = null;
        _service.FastModeChanged += (_, e) => eventArgs = e;

        _service.Activate();

        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.IsFastModeActive);
        Assert.Equal(DefaultFastModelId, eventArgs.ActiveModelId);
        Assert.Equal(DefaultModelId, eventArgs.InactiveModelId);
    }

    [Fact]
    public void Deactivate_Should_Clear_IsActive()
    {
        _service.Activate();
        _service.Deactivate();

        Assert.False(_service.IsFastModeActive);
    }

    [Fact]
    public void Deactivate_Should_Raise_Event()
    {
        _service.Activate();
        FastModeChangedEventArgs? eventArgs = null;
        _service.FastModeChanged += (_, e) => eventArgs = e;

        _service.Deactivate();

        Assert.NotNull(eventArgs);
        Assert.False(eventArgs.IsFastModeActive);
        Assert.Equal(DefaultModelId, eventArgs.ActiveModelId);
        Assert.Equal(DefaultFastModelId, eventArgs.InactiveModelId);
    }

    [Fact]
    public void Toggle_Should_Switch_State()
    {
        Assert.False(_service.IsFastModeActive);

        _service.Toggle();
        Assert.True(_service.IsFastModeActive);

        _service.Toggle();
        Assert.False(_service.IsFastModeActive);
    }

    [Fact]
    public void SetFastModel_Should_Update_FastModelId()
    {
        _service.SetFastModel("gpt-4.1-nano");

        Assert.Equal("gpt-4.1-nano", _service.FastModelId);
    }

    [Fact]
    public void SetPrimaryModel_Should_Update_PrimaryModelId()
    {
        _service.SetPrimaryModel("gpt-4.1");

        Assert.Equal("gpt-4.1", _service.PrimaryModelId);
    }

    [Fact]
    public void SetFastModel_Should_Reject_Empty()
    {
        Assert.Throws<ArgumentException>(() => _service.SetFastModel(""));
    }

    [Fact]
    public void SetPrimaryModel_Should_Reject_Empty()
    {
        Assert.Throws<ArgumentException>(() => _service.SetPrimaryModel(""));
    }

    [Fact]
    public void GetCurrentModelId_Should_Return_Primary_When_Inactive()
    {
        Assert.Equal(DefaultModelId, _service.GetCurrentModelId());
    }

    [Fact]
    public void GetCurrentModelId_Should_Return_Fast_When_Active()
    {
        _service.Activate();

        Assert.Equal(DefaultFastModelId, _service.GetCurrentModelId());
    }

    [Fact]
    public async Task Cooldown_Should_Auto_Deactivate()
    {
        using var deactivatedSignal = new SemaphoreSlim(0, 1);
        _service.FastModeChanged += (_, e) =>
        {
            if (!e.IsFastModeActive) deactivatedSignal.Release();
        };

        _service.Activate();
        Assert.True(_service.IsFastModeActive);

        // 等待 cooldown 定时器触发 Deactivate 事件，替代轮询
        await deactivatedSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.False(_service.IsFastModeActive);
    }

    [Fact]
    public void Activate_Idempotent_Should_Not_Raise_Duplicate_Events()
    {
        var eventCount = 0;
        _service.FastModeChanged += (_, _) => eventCount++;

        _service.Activate();
        _service.Activate();

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void Deactivate_When_Not_Active_Should_Not_Raise_Event()
    {
        var eventCount = 0;
        _service.FastModeChanged += (_, _) => eventCount++;

        _service.Deactivate();

        Assert.Equal(0, eventCount);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
