namespace Core.Configuration.Tests;

public sealed class SimpleModeServiceTests
{
    private readonly SimpleModeService _service;

    public SimpleModeServiceTests()
    {
        _service = new SimpleModeService();
    }

    [Fact]
    public void Initial_State_Should_Not_Be_SimpleMode()
    {
        Assert.False(_service.IsSimpleMode);
    }

    [Fact]
    public void Initial_Config_Should_Be_Default()
    {
        var config = _service.GetCurrentConfig();

        Assert.True(config.UseSimplePrompts);
        Assert.True(config.ReduceToolSet);
        Assert.True(config.MinimalUI);
        Assert.True(config.AutoConfirm);
        Assert.False(config.HideSpinner);
        Assert.False(config.HideStatusBar);
    }

    [Fact]
    public void Enable_Should_Set_IsSimpleMode()
    {
        _service.Enable();

        Assert.True(_service.IsSimpleMode);
    }

    [Fact]
    public void Enable_Should_Raise_Event()
    {
        SimpleModeChangedEventArgs? eventArgs = null;
        _service.SimpleModeChanged += (_, e) => eventArgs = e;

        _service.Enable();

        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.IsSimpleMode);
        Assert.True(eventArgs.Config.UseSimplePrompts);
    }

    [Fact]
    public void Enable_Idempotent_Should_Not_Raise_Duplicate_Events()
    {
        var eventCount = 0;
        _service.SimpleModeChanged += (_, _) => eventCount++;

        _service.Enable();
        _service.Enable();

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void Disable_Should_Clear_IsSimpleMode()
    {
        _service.Enable();
        _service.Disable();

        Assert.False(_service.IsSimpleMode);
    }

    [Fact]
    public void Disable_Should_Raise_Event()
    {
        _service.Enable();
        SimpleModeChangedEventArgs? eventArgs = null;
        _service.SimpleModeChanged += (_, e) => eventArgs = e;

        _service.Disable();

        Assert.NotNull(eventArgs);
        Assert.False(eventArgs.IsSimpleMode);
    }

    [Fact]
    public void Disable_When_Not_Active_Should_Not_Raise_Event()
    {
        var eventCount = 0;
        _service.SimpleModeChanged += (_, _) => eventCount++;

        _service.Disable();

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void Toggle_Should_Switch_State()
    {
        Assert.False(_service.IsSimpleMode);

        _service.Toggle();
        Assert.True(_service.IsSimpleMode);

        _service.Toggle();
        Assert.False(_service.IsSimpleMode);
    }

    [Fact]
    public void Toggle_Should_Return_New_State()
    {
        var result = _service.Toggle();
        Assert.True(result);

        result = _service.Toggle();
        Assert.False(result);
    }

    [Fact]
    public void UpdateConfig_Should_Update_Config()
    {
        var newConfig = new SimpleModeConfig
        {
            UseSimplePrompts = false,
            ReduceToolSet = false,
            MinimalUI = false,
            AutoConfirm = false,
            HideSpinner = true,
            HideStatusBar = true
        };

        _service.UpdateConfig(newConfig);

        var config = _service.GetCurrentConfig();
        Assert.False(config.UseSimplePrompts);
        Assert.False(config.ReduceToolSet);
        Assert.False(config.MinimalUI);
        Assert.False(config.AutoConfirm);
        Assert.True(config.HideSpinner);
        Assert.True(config.HideStatusBar);
    }

    [Fact]
    public void UpdateConfig_Should_Raise_Event()
    {
        SimpleModeChangedEventArgs? eventArgs = null;
        _service.SimpleModeChanged += (_, e) => eventArgs = e;

        var newConfig = new SimpleModeConfig { HideSpinner = true };
        _service.UpdateConfig(newConfig);

        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.Config.HideSpinner);
    }

    [Fact]
    public void UpdateConfig_Should_Throw_On_Null()
    {
        Assert.Throws<ArgumentNullException>(() => _service.UpdateConfig(null!));
    }

    [Fact]
    public void Enable_Should_Also_Enable_BriefMode()
    {
        var briefModeService = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new SimpleModeService(briefModeService: briefModeService);

        service.Enable();

        Assert.True(briefModeService.IsEnabled);
    }

    [Fact]
    public void Disable_Should_Also_Disable_BriefMode()
    {
        var briefModeService = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new SimpleModeService(briefModeService: briefModeService);

        service.Enable();
        service.Disable();

        Assert.False(briefModeService.IsEnabled);
    }

    [Fact]
    public void Without_BriefModeService_Enable_Should_Not_Throw()
    {
        var service = new SimpleModeService(briefModeService: null);

        var exception = Record.Exception(() => service.Enable());

        Assert.Null(exception);
    }

    [Fact]
    public void SimpleModeConfig_Default_Should_Have_Expected_Values()
    {
        var config = SimpleModeConfig.Default;

        Assert.True(config.UseSimplePrompts);
        Assert.True(config.ReduceToolSet);
        Assert.True(config.MinimalUI);
        Assert.True(config.AutoConfirm);
        Assert.False(config.HideSpinner);
        Assert.False(config.HideStatusBar);
    }
}
