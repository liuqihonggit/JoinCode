namespace JoinCode.CliCommands;

/// <summary>
/// 子命令门禁 — 按当前 profile 的 <c>cli-commands</c> 消费点决定某条子命令是否可用。
/// <para>
/// TASK100 S4：命令表面的启停此前只能靠改代码/改编译宏。现在由 profile 决定：
/// 删掉一条节点声明，命令就从 CLI 表面消失（而不是「未知子命令」那种看不出原因的错误）。
/// </para>
/// <para>
/// 语义完全复用 <see cref="Abstractions.Composition.Profile.ConsumerDeclaration"/>：
/// 未声明整个 <c>cli-commands</c> 段 = 全开（老 profile 不必逐条补声明），声明了 = 白名单。
/// </para>
/// <para>
/// 为什么门禁失败要 fail-open：子命令分发发生在 DI 容器构建之前，profile 读不到时整条
/// CLI 会连 <c>--help</c> 之外的命令都不可用 —— 门禁是「裁剪」手段，不该变成「停机」原因。
/// 失败会打 <c>[CLI-GATE]</c> 诊断行，便于排查。
/// </para>
/// </summary>
internal static class CliCommandGate {
    private const string DefaultProfile = "base";

    private static readonly string s_profileName = ResolveProfileName(Environment.GetCommandLineArgs());
    private static readonly Lazy<Abstractions.Composition.Profile.ResolvedProfile?> s_profile = new(Load);

    /// <summary>当前生效的 profile 名（用于报错措辞）。</summary>
    public static string ProfileName => s_profileName;

    /// <summary>
    /// 判断某节点在当前 profile 是否启用。
    /// </summary>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public static bool IsEnabled(string nodeId) =>
        Abstractions.Composition.Profile.ConsumerDeclaration.IsEnabled(s_profile.Value, CliCommandNodes.ConsumerId, nodeId);

    /// <summary>
    /// 从命令行/环境变量解析 profile 名：<c>--profile X</c> &gt; <c>--profile=X</c> &gt; <c>JCC_NODE_PROFILE</c> &gt; base。
    /// </summary>
    /// <param name="args">进程参数（首元素为可执行文件路径）。</param>
    /// <returns>profile 名。</returns>
    private static string ResolveProfileName(string[] args) {
        var flag = JccCliArgEnumConstants.Profile;

        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (string.Equals(arg, flag, StringComparison.Ordinal)) {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return args[i + 1];
                continue;
            }
            var prefix = flag + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg[prefix.Length..];
        }

        var fromEnv = Environment.GetEnvironmentVariable(JccEnvVar.NodeProfile.ToValue());
        return string.IsNullOrWhiteSpace(fromEnv) ? DefaultProfile : fromEnv;
    }

    /// <summary>读取并解析 profile（只读一次；任何异常都退回「全开」）。</summary>
    /// <returns>解析结果；读不到时 <see langword="null"/>（= 全开）。</returns>
    private static Abstractions.Composition.Profile.ResolvedProfile? Load() {
        try {
            var profiles = Composition.NodeRegistry.NodeProfileLoader.LoadFromResources(
                fileSystem: new IO.FileSystem.PhysicalFileSystem());
            if (!profiles.ContainsKey(s_profileName))
                return null; // 未知 profile 由常规路径（引擎装配）报错，门禁不抢这个错误

            return new Composition.NodeRegistry.NodeProfileResolver(profiles).Resolve(s_profileName);
        } catch (Exception ex) {
            Diag.WriteLine($"[CLI-GATE] profile '{s_profileName}' 读取失败，命令表面按全开处理: {ex.Message}");
            return null;
        }
    }
}
