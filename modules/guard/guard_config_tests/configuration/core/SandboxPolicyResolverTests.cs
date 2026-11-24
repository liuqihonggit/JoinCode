namespace Guard.Tests.Configuration;

/// <summary>
/// SandboxPolicyResolver 测试 — managed-settings 策略投影：
/// 锁定键、allowManagedDomainsOnly / allowManagedReadPathsOnly 的「只认 policy 源」语义。
/// </summary>
public sealed class SandboxPolicyResolverTests {
    private static SettingsJson PolicyWith(SandboxSettings sandbox) => new() {
        Current = new CurrentSettings { Sandbox = sandbox },
    };

    [Fact]
    public void Given_Policy定义三个可锁键_When_Apply_Then_锁定并列出键() {
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Enabled = true,
                AutoAllowBashIfSandboxed = false,
                AllowUnsandboxedCommands = false,
            },
        };
        var policy = PolicyWith(new SandboxSettings {
            Enabled = true,
            AutoAllowBashIfSandboxed = false,
            AllowUnsandboxedCommands = false,
        });

        var result = SandboxPolicyResolver.Apply(config, policy);

        result.SandboxPolicy.Locked.Should().BeTrue();
        result.SandboxPolicy.LockedKeys.Should().BeEquivalentTo([
            SandboxPolicyResolver.KeyEnabled,
            SandboxPolicyResolver.KeyAutoAllow,
            SandboxPolicyResolver.KeyAllowUnsandboxed,
        ]);
        result.SandboxPolicy.PolicyLockedKeys.Should().BeEquivalentTo(result.SandboxPolicy.LockedKeys);
        result.SandboxPolicy.FlagLockedKeys.Should().BeEmpty();
        result.Sandbox.Enabled.Should().BeTrue("策略值已由 5 源合并进入 SandboxConfig，投影不得反向改值");
        result.Sandbox.AutoAllowBashIfSandboxed.Should().BeFalse("这里是合并结果，AutoAllow 的值由 SettingsMapper 负责");
    }

    [Fact]
    public void Given_Policy只定义network_When_Apply_Then_不锁定() {
        var config = new WorkflowConfig();
        var policy = PolicyWith(new SandboxSettings {
            Network = new SandboxNetworkSettings { AllowManagedDomainsOnly = true, AllowedDomains = ["policy.example"] },
        });

        var result = SandboxPolicyResolver.Apply(config, policy);

        result.SandboxPolicy.Locked.Should().BeFalse("network 子键不参与本地修改锁");
        result.SandboxPolicy.AllowManagedDomainsOnly.Should().BeTrue();
    }

    [Fact]
    public void Given_Policy允许托管域名_When_Apply_Then_allowedDomains只取policy源() {
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Network = new SandboxNetworkConfig { AllowedDomains = ["user.example", "project.example"] },
            },
        };
        var policy = PolicyWith(new SandboxSettings {
            Network = new SandboxNetworkSettings {
                AllowManagedDomainsOnly = true,
                AllowedDomains = ["policy.example"],
            },
        });

        var result = SandboxPolicyResolver.Apply(config, policy);

        result.Sandbox.Network.AllowManagedDomainsOnly.Should().BeTrue();
        result.Sandbox.Network.AllowedDomains.Should().Equal(["policy.example"]);
        result.SandboxPolicy.AllowManagedDomainsOnly.Should().BeTrue();
    }

    [Fact]
    public void Given_Policy允许托管读路径_When_Apply_Then_allowRead只取policy源() {
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Filesystem = new SandboxFilesystemConfig {
                    AllowRead = ["/user/read", "/project/read"],
                    DenyRead = ["/secret"],
                },
            },
        };
        var policy = PolicyWith(new SandboxSettings {
            Filesystem = new SandboxFilesystemSettings {
                AllowManagedReadPathsOnly = true,
                AllowRead = ["/policy/read"],
            },
        });

        var result = SandboxPolicyResolver.Apply(config, policy);

        result.Sandbox.Filesystem.AllowManagedReadPathsOnly.Should().BeTrue();
        result.Sandbox.Filesystem.AllowRead.Should().Equal(["/policy/read"]);
        result.Sandbox.Filesystem.DenyRead.Should().Equal(["/secret"], "denyRead 不在托管限定范围内，保持合并结果");
        result.SandboxPolicy.AllowManagedReadPathsOnly.Should().BeTrue();
    }

    [Fact]
    public void Given_Flag定义锁定键_When_Apply_Then_锁定但managed列表仍只认policy() {
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Network = new SandboxNetworkConfig {
                    AllowManagedDomainsOnly = true,
                    AllowedDomains = ["user.example"],
                },
            },
        };
        var flag = PolicyWith(new SandboxSettings {
            Enabled = true,
            Network = new SandboxNetworkSettings {
                AllowManagedDomainsOnly = true,
                AllowedDomains = ["flag.example"],
            },
        });

        var result = SandboxPolicyResolver.Apply(config, policySettings: null, flagSettings: flag);

        result.SandboxPolicy.Locked.Should().BeTrue("flagSettings 与 policySettings 都高于 localSettings，必须锁本地修改");
        result.SandboxPolicy.LockedKeys.Should().Contain(SandboxPolicyResolver.KeyEnabled);
        result.SandboxPolicy.FlagLockedKeys.Should().Equal([SandboxPolicyResolver.KeyEnabled]);
        result.SandboxPolicy.PolicyLockedKeys.Should().BeEmpty();
        result.SandboxPolicy.AllowManagedDomainsOnly.Should().BeFalse("managed 开关只认 policy 源，flag 不得伪造");
        result.Sandbox.Network.AllowManagedDomainsOnly.Should().BeFalse();
        result.Sandbox.Network.AllowedDomains.Should().Contain("user.example");
    }

    [Fact]
    public void Given_Flag和Policy重复定义同一键_When_Apply_Then_锁定键不重复() {
        var config = new WorkflowConfig();
        var flag = PolicyWith(new SandboxSettings { Enabled = true });
        var policy = PolicyWith(new SandboxSettings { Enabled = false });

        var result = SandboxPolicyResolver.Apply(config, policy, flag);

        result.SandboxPolicy.LockedKeys.Should().Equal([SandboxPolicyResolver.KeyEnabled]);
        result.SandboxPolicy.FlagLockedKeys.Should().Equal([SandboxPolicyResolver.KeyEnabled]);
        result.SandboxPolicy.PolicyLockedKeys.Should().Equal([SandboxPolicyResolver.KeyEnabled]);
    }

    [Fact]
    public void Given_无Policy但用户侧伪造managed开关_When_Apply_Then_开关失效() {
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Network = new SandboxNetworkConfig {
                    AllowManagedDomainsOnly = true,
                    AllowedDomains = ["user.example"],
                },
                Filesystem = new SandboxFilesystemConfig {
                    AllowManagedReadPathsOnly = true,
                    AllowRead = ["/user/read"],
                },
            },
        };

        var result = SandboxPolicyResolver.Apply(config, policySettings: null);

        result.Sandbox.Network.AllowManagedDomainsOnly.Should().BeFalse("managed 开关只在 policySettings 中生效");
        result.Sandbox.Filesystem.AllowManagedReadPathsOnly.Should().BeFalse();
        result.Sandbox.Network.AllowedDomains.Should().Equal(["user.example"], "非托管模式下保留用户域名");
        result.Sandbox.Filesystem.AllowRead.Should().Equal(["/user/read"]);
        result.SandboxPolicy.Locked.Should().BeFalse();
    }
}
