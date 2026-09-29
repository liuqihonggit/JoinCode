namespace Core.Tests.DependencyInjection;

/// <summary>
/// ServiceRegistrationConfigValidator 确定性测试 — ValidateFileOperationConfig / ValidateShellExecutionConfig / ApplyEnvOverrides 纯计算。
/// </summary>
public sealed class ServiceRegistrationConfigTests {
    // ===== ValidateFileOperationConfig =====

    [Fact]
    public void ValidateFileOperationConfig_DefaultConfig_ReturnsTrue() {
        var config = new FileOperationConfig();
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeTrue();
    }

    [Fact]
    public void ValidateFileOperationConfig_MaxReadSizeBelowMin_ReturnsFalse() {
        var config = new FileOperationConfig { MaxReadSize = 1023 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_MaxReadSizeAtMin_ReturnsTrue() {
        var config = new FileOperationConfig { MaxReadSize = 1024 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeTrue();
    }

    [Fact]
    public void ValidateFileOperationConfig_MaxReadSizeAboveMax_ReturnsFalse() {
        var config = new FileOperationConfig { MaxReadSize = 1024L * 1024 * 1024 + 1 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_MaxWriteSizeBelowMin_ReturnsFalse() {
        var config = new FileOperationConfig { MaxWriteSize = 1023 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_MaxWriteSizeAboveMax_ReturnsFalse() {
        var config = new FileOperationConfig { MaxWriteSize = 1024 * 1024 * 1024 + 1 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_BufferSizeBelowMin_ReturnsFalse() {
        var config = new FileOperationConfig { BufferSize = 511 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_BufferSizeAboveMax_ReturnsFalse() {
        var config = new FileOperationConfig { BufferSize = 1024 * 1024 + 1 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_BinaryDetectionBufferSizeBelowMin_ReturnsFalse() {
        var config = new FileOperationConfig { BinaryDetectionBufferSize = 1023 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_BinaryDetectionBufferSizeAboveMax_ReturnsFalse() {
        var config = new FileOperationConfig { BinaryDetectionBufferSize = 64 * 1024 + 1 };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateFileOperationConfig_AllBoundariesAtExactLimits_ReturnsTrue() {
        var config = new FileOperationConfig {
            MaxReadSize = 1024,
            MaxWriteSize = 1024,
            BufferSize = 512,
            BinaryDetectionBufferSize = 1024
        };
        ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config).Should().BeTrue();
    }

    // ===== ValidateShellExecutionConfig =====

    [Fact]
    public void ValidateShellExecutionConfig_DefaultConfig_ReturnsTrue() {
        var config = new ShellExecutionConfig();
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeTrue();
    }

    [Fact]
    public void ValidateShellExecutionConfig_MaxOutputBytesBelowMin_ReturnsFalse() {
        var config = new ShellExecutionConfig { MaxOutputBytes = 1023 };
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateShellExecutionConfig_MaxOutputBytesAboveMax_ReturnsFalse() {
        var config = new ShellExecutionConfig { MaxOutputBytes = 1024 * 1024 + 1 };
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateShellExecutionConfig_DefaultTimeoutSecondsBelowMin_ReturnsFalse() {
        var config = new ShellExecutionConfig { DefaultTimeoutSeconds = 0 };
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateShellExecutionConfig_DefaultTimeoutSecondsAboveMax_ReturnsFalse() {
        var config = new ShellExecutionConfig { DefaultTimeoutSeconds = 3601 };
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeFalse();
    }

    [Fact]
    public void ValidateShellExecutionConfig_AllBoundariesAtExactLimits_ReturnsTrue() {
        var config = new ShellExecutionConfig {
            MaxOutputBytes = 1024,
            DefaultTimeoutSeconds = 1
        };
        ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config).Should().BeTrue();
    }

    // ===== ApplyEnvOverrides =====

    [Fact]
    public void ApplyEnvOverrides_NullBoth_KeepsOriginalValues() {
        var config = new ShellExecutionConfig();
        var originalAbsolute = config.AbsoluteTimeoutSeconds;
        var originalResume = config.ResumeTimeoutSeconds;
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, null, null);
        config.AbsoluteTimeoutSeconds.Should().Be(originalAbsolute);
        config.ResumeTimeoutSeconds.Should().Be(originalResume);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("3600", 3600)]
    public void ApplyEnvOverrides_ValidAbsolute_OverwritesAbsoluteTimeout(string envValue, int expected) {
        var config = new ShellExecutionConfig();
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, envValue, null);
        config.AbsoluteTimeoutSeconds.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    public void ApplyEnvOverrides_InvalidAbsolute_KeepsOriginal(string envValue) {
        var config = new ShellExecutionConfig();
        var original = config.AbsoluteTimeoutSeconds;
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, envValue, null);
        config.AbsoluteTimeoutSeconds.Should().Be(original);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("120", 120)]
    [InlineData("3600", 3600)]
    public void ApplyEnvOverrides_ValidResume_OverwritesResumeTimeout(string envValue, int expected) {
        var config = new ShellExecutionConfig();
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, null, envValue);
        config.ResumeTimeoutSeconds.Should().Be(expected);
    }

    [Theory]
    [InlineData("59")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void ApplyEnvOverrides_InvalidResume_KeepsOriginal(string envValue) {
        var config = new ShellExecutionConfig();
        var original = config.ResumeTimeoutSeconds;
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, null, envValue);
        config.ResumeTimeoutSeconds.Should().Be(original);
    }

    [Fact]
    public void ApplyEnvOverrides_BothValid_OverwritesBoth() {
        var config = new ShellExecutionConfig();
        ServiceRegistrationConfigValidator.ApplyEnvOverrides(config, "200", "300");
        config.AbsoluteTimeoutSeconds.Should().Be(200);
        config.ResumeTimeoutSeconds.Should().Be(300);
    }
}
