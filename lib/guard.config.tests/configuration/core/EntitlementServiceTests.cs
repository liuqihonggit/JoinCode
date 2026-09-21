namespace Core.Configuration.Tests;

public sealed class EntitlementServiceTests {
    [Fact]
    public async Task IsBriefEntitled_Default_Should_Be_True() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);
        Assert.True(service.IsBriefEntitled);
    }

    [Fact]
    public async Task IsBriefEntitled_EnvVar_True_Should_Be_True() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);
        // JCC_BRIEF=1 应该允许
        using var envScope1 = EnvVarScope.Set(JccEnvVarEnumConstants.Brief, "1");
        Assert.True(service.IsBriefEntitled);
    }

    [Fact]
    public async Task IsBriefEntitled_EnvVar_False_Should_Be_False() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);
        // JCC_BRIEF=false 应该拒绝
        using var envScope2 = EnvVarScope.Set(JccEnvVarEnumConstants.Brief, "false");
        Assert.False(service.IsBriefEntitled);
    }

    [Fact]
    public async Task IsBriefEntitled_EnvVar_Zero_Should_Be_False() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);
        // JCC_BRIEF=0 应该拒绝
        using var envScope3 = EnvVarScope.Set(JccEnvVarEnumConstants.Brief, "0");
        Assert.False(service.IsBriefEntitled);
    }

    [Fact]
    public async Task IsBriefEnabled_Requires_Entitlement_And_OptIn() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);

        // 默认: entitlement=true, optIn=false → enabled=false
        Assert.False(service.IsBriefEnabled);

        // 启用后: entitlement=true, optIn=true → enabled=true
        briefMode.Enable();
        Assert.True(service.IsBriefEnabled);
    }

    [Fact]
    public async Task IsBriefEnabled_EnvVar_False_Overrides_OptIn() {
        await using var briefMode = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        await using var service = new EntitlementService(briefMode);
        briefMode.Enable();

        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.Brief, "false");
        try {
            Assert.False(service.IsBriefEnabled);
        } finally {
            Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.Brief, null);
        }
    }

    [Fact]
    public void Constructor_NullBriefMode_Should_Throw() {
        Assert.Throws<ArgumentNullException>(() => new EntitlementService(null!));
    }
}

public sealed class BriefModeServiceTests {
    [Fact]
    public async Task Initial_State_Should_Be_Disabled() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        Assert.False(service.IsEnabled);
        Assert.Null(service.EnabledAt);
        Assert.False(service.UserMsgOptIn);
    }

    [Fact]
    public async Task Enable_Should_Set_IsEnabled_And_UserMsgOptIn() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        Assert.True(service.IsEnabled);
        Assert.True(service.UserMsgOptIn);
        Assert.NotNull(service.EnabledAt);
    }

    [Fact]
    public async Task Disable_Should_Clear_IsEnabled_And_UserMsgOptIn() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        service.Disable();
        Assert.False(service.IsEnabled);
        Assert.False(service.UserMsgOptIn);
        Assert.Null(service.EnabledAt);
    }

    [Fact]
    public async Task Toggle_Should_Switch_Both_States() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
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
    public async Task UserMsgOptIn_Can_Be_Set_Independently() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.UserMsgOptIn = true;
        Assert.True(service.UserMsgOptIn);
        Assert.False(service.IsEnabled); // IsEnabled 不受 UserMsgOptIn 影响
    }

    [Fact]
    public async Task GetStatus_Enabled_Should_Return_EnabledStatus() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        service.Enable();
        var status = service.GetStatus();
        Assert.True(status.IsEnabled);
        Assert.NotNull(status.EnabledAt);
    }

    [Fact]
    public async Task GetStatus_Disabled_Should_Return_DisabledStatus() {
        await using var service = new BriefModeService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var status = service.GetStatus();
        Assert.False(status.IsEnabled);
        Assert.Null(status.EnabledAt);
    }

    [Fact]
    public async Task CrossInstance_PersistedToFile_WhenFileSystemProvided() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var cwd = fs.GetCurrentDirectory();
        fs.CreateDirectory(Path.Combine(cwd, ".git"));
        var clock = JoinCode.Abstractions.Clock.SystemClockService.Instance;
        await using var serviceA = new BriefModeService(clock, fs);
        serviceA.Enable();
        Assert.True(serviceA.IsEnabled);
        await using var serviceB = new BriefModeService(clock, fs);
        Assert.True(serviceB.IsEnabled, "新实例应从文件加载 enabled 状态");
        Assert.NotNull(serviceB.EnabledAt);

        serviceB.Disable();
        await using var serviceC = new BriefModeService(clock, fs);
        Assert.False(serviceC.IsEnabled, "新实例应从文件加载 disabled 状态");
    }
}