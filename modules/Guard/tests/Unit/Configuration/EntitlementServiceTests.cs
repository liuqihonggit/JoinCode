namespace Core.Configuration.Tests;

public sealed class EntitlementServiceTests
{
    [Fact]
    public void IsBriefEntitled_Default_Should_Be_True()
    {
        // 开源项目默认允许
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);
        Assert.True(service.IsBriefEntitled);
    }

    [Fact]
    public void IsBriefEntitled_EnvVar_True_Should_Be_True()
    {
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);
        // JCC_BRIEF=1 应该允许
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, "1");
        try
        {
            Assert.True(service.IsBriefEntitled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, null);
        }
    }

    [Fact]
    public void IsBriefEntitled_EnvVar_False_Should_Be_False()
    {
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);
        // JCC_BRIEF=false 应该拒绝
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, "false");
        try
        {
            Assert.False(service.IsBriefEntitled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, null);
        }
    }

    [Fact]
    public void IsBriefEntitled_EnvVar_Zero_Should_Be_False()
    {
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);
        // JCC_BRIEF=0 应该拒绝
        Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, "0");
        try
        {
            Assert.False(service.IsBriefEntitled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, null);
        }
    }

    [Fact]
    public void IsBriefEnabled_Requires_Entitlement_And_OptIn()
    {
        // 对齐 TS: (getKairosActive() || getUserMsgOptIn()) && isBriefEntitled()
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);

        // 默认: entitlement=true, optIn=false → enabled=false
        Assert.False(service.IsBriefEnabled);

        // 启用后: entitlement=true, optIn=true → enabled=true
        briefMode.Enable();
        Assert.True(service.IsBriefEnabled);
    }

    [Fact]
    public void IsBriefEnabled_EnvVar_False_Overrides_OptIn()
    {
        // JCC_BRIEF=false 即使 optIn=true 也应该拒绝
        var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var service = new EntitlementService(briefMode);
        briefMode.Enable();

        Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, "false");
        try
        {
            Assert.False(service.IsBriefEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JccEnvVarConstants.Brief, null);
        }
    }

    [Fact]
    public void Constructor_NullBriefMode_Should_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new EntitlementService(null!));
    }
}

public sealed class BriefModeServiceTests
{
    [Fact]
    public void Initial_State_Should_Be_Disabled()
    {
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        Assert.False(service.IsEnabled);
        Assert.Null(service.EnabledAt);
        Assert.False(service.UserMsgOptIn);
    }

    [Fact]
    public void Enable_Should_Set_IsEnabled_And_UserMsgOptIn()
    {
        // 对齐 TS: setUserMsgOptIn(true)
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        Assert.True(service.IsEnabled);
        Assert.True(service.UserMsgOptIn);
        Assert.NotNull(service.EnabledAt);
    }

    [Fact]
    public void Disable_Should_Clear_IsEnabled_And_UserMsgOptIn()
    {
        // 对齐 TS: setUserMsgOptIn(false)
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        service.Disable();
        Assert.False(service.IsEnabled);
        Assert.False(service.UserMsgOptIn);
        Assert.Null(service.EnabledAt);
    }

    [Fact]
    public void Toggle_Should_Switch_Both_States()
    {
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        Assert.False(service.IsEnabled);
        Assert.False(service.UserMsgOptIn);

        service.Toggle();
        Assert.True(service.IsEnabled);
        Assert.True(service.UserMsgOptIn);

        service.Toggle();
        Assert.False(service.IsEnabled);
        Assert.False(service.UserMsgOptIn);
    }

    [Fact]
    public void UserMsgOptIn_Can_Be_Set_Independently()
    {
        // 对齐 TS: userMsgOptIn 可独立于 isBriefOnly 设置
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.UserMsgOptIn = true;
        Assert.True(service.UserMsgOptIn);
        Assert.False(service.IsEnabled); // IsEnabled 不受 UserMsgOptIn 影响
    }

    [Fact]
    public void GetStatus_Enabled_Should_Return_EnabledStatus()
    {
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        var status = service.GetStatus();
        Assert.True(status.IsEnabled);
        Assert.NotNull(status.EnabledAt);
    }

    [Fact]
    public void GetStatus_Disabled_Should_Return_DisabledStatus()
    {
        var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var status = service.GetStatus();
        Assert.False(status.IsEnabled);
        Assert.Null(status.EnabledAt);
    }
}
