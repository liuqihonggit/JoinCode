namespace Core.Configuration;

/// <summary>
/// 沙箱策略投影 — 把 managed-settings（policySettings）与 <c>--settings</c>（flagSettings）里的 sandbox 策略落到运行配置。
/// <para>合并后的 <c>SettingsJson</c> 已丢失来源信息，因此这里**分别**消费 policy / flag 快照：</para>
/// <list type="number">
/// <item>policy 或 flag 显式定义 <c>enabled/autoAllowBashIfSandboxed/allowUnsandboxedCommands</c> → 锁定本地 <c>/sandbox</c> 修改；</item>
/// <item><c>allowManagedDomainsOnly=true</c> → <c>network.allowedDomains</c> 只保留 policy 源；</item>
/// <item><c>allowManagedReadPathsOnly=true</c> → <c>filesystem.allowRead</c> 只保留 policy 源。</item>
/// </list>
/// <para>非 policy 源写这两个 managed 开关不生效（对齐基准「仅在 managed settings 中生效」）。</para>
/// </summary>
public static class SandboxPolicyResolver {
    /// <summary>锁定键 — sandbox.enabled。</summary>
    public const string KeyEnabled = "sandbox.enabled";

    /// <summary>锁定键 — sandbox.autoAllowBashIfSandboxed。</summary>
    public const string KeyAutoAllow = "sandbox.autoAllowBashIfSandboxed";

    /// <summary>锁定键 — sandbox.allowUnsandboxedCommands。</summary>
    public const string KeyAllowUnsandboxed = "sandbox.allowUnsandboxedCommands";

    /// <summary>
    /// 把 policy / flag 策略投影到工作流配置 — 无 policy 时清除用户侧伪造的 managed 开关。
    /// </summary>
    /// <param name="config">已合并用户/项目/本地/flag 的配置。</param>
    /// <param name="policySettings">managed-settings.json 的独立快照（null = 无策略文件）。</param>
    /// <param name="flagSettings">--settings 的独立快照（null = 无 flag 来源）。</param>
    /// <returns>投影后的配置。</returns>
    public static WorkflowConfig Apply(WorkflowConfig config, SettingsJson? policySettings, SettingsJson? flagSettings = null) {
        ArgumentNullException.ThrowIfNull(config);

        var policy = policySettings?.Current?.Sandbox;
        var policyNetwork = policy?.Network;
        var policyFilesystem = policy?.Filesystem;
        var flagLockedKeys = new List<string>(3);
        var policyLockedKeys = new List<string>(3);
        CollectLockedKeys(flagSettings?.Current?.Sandbox, flagLockedKeys);
        CollectLockedKeys(policy, policyLockedKeys);
        var lockedKeys = new List<string>(6);
        foreach (var key in flagLockedKeys) AddIfAbsent(lockedKeys, true, key);
        foreach (var key in policyLockedKeys) AddIfAbsent(lockedKeys, true, key);
        Diag.WriteLine($"[SANDBOX-POLICY] locked={lockedKeys.Count > 0} keys=[{string.Join(", ", lockedKeys)}]");
        var managedDomainsOnly = policyNetwork?.AllowManagedDomainsOnly == true;
        var managedReadPathsOnly = policyFilesystem?.AllowManagedReadPathsOnly == true;

        var network = config.Sandbox.Network with {
            AllowManagedDomainsOnly = managedDomainsOnly,
            AllowedDomains = managedDomainsOnly && policyNetwork is not null
                ? [.. policyNetwork.AllowedDomains]
                : [.. config.Sandbox.Network.AllowedDomains],
        };

        var filesystem = config.Sandbox.Filesystem with {
            AllowManagedReadPathsOnly = managedReadPathsOnly,
            AllowRead = managedReadPathsOnly && policyFilesystem is not null
                ? [.. policyFilesystem.AllowRead]
                : [.. config.Sandbox.Filesystem.AllowRead],
        };

        return config with {
            Sandbox = config.Sandbox with { Network = network, Filesystem = filesystem },
            SandboxPolicy = new SandboxPolicyConfig {
                Locked = lockedKeys.Count > 0,
                LockedKeys = lockedKeys,
                FlagLockedKeys = flagLockedKeys,
                PolicyLockedKeys = policyLockedKeys,
                AllowManagedDomainsOnly = managedDomainsOnly,
                AllowManagedReadPathsOnly = managedReadPathsOnly,
            },
        };
    }

    /// <summary>收集来源显式定义的锁定键 — 只有这三个键会锁本地修改（对齐基准 areSandboxSettingsLockedByPolicy）。</summary>
    /// <param name="source">flag 或 policy 的 sandbox 设置。</param>
    /// <param name="keys">累计锁定键（同键不重复）。</param>
    private static void CollectLockedKeys(SandboxSettings? source, List<string> keys) {
        if (source is null) {
            return;
        }

        AddIfAbsent(keys, source.Enabled is not null, KeyEnabled);
        AddIfAbsent(keys, source.AutoAllowBashIfSandboxed is not null, KeyAutoAllow);
        AddIfAbsent(keys, source.AllowUnsandboxedCommands is not null, KeyAllowUnsandboxed);
    }

    /// <summary>命中时追加锁定键，并保持首次出现顺序。</summary>
    private static void AddIfAbsent(List<string> keys, bool defined, string key) {
        if (defined && !keys.Contains(key)) {
            keys.Add(key);
        }
    }
}
