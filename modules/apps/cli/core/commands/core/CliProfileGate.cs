namespace JoinCode.CliCommands;

/// <summary>
/// CLI 侧 profile 门禁 — 一个进程只解析一次 profile，供各消费点判断节点启停。
/// <para>
/// TASK100 S6：S4 为命令表面写的 profile 读取逻辑（<c>--profile</c> &gt; 环境变量 &gt; base，
/// 失败 fail-open 并打 <c>[CLI-GATE]</c>）本来只服务 <c>cli-commands</c> 一个消费点。
/// 工具组（<c>tool-groups</c>）需要完全相同的规则 —— 两处各写一遍，迟早出现
/// 「命令按 profile 裁、工具组不裁」这种看得见的不一致。因此把这层抽出来，
/// 判定语义仍然只有一份：<see cref="Abstractions.Composition.Profile.ConsumerDeclaration"/>。
/// </para>
/// <para>
/// 为什么 fail-open 而不是 fail-closed：门禁发生在 DI 容器构建之前，profile 读不到时
/// 整条 CLI 会连 <c>--help</c> 之外的命令都不可用。门禁是「裁剪」手段，不该变成「停机」原因。
/// </para>
/// </summary>
internal static class CliProfileGate {
    private const string DefaultProfile = "base";

    private static readonly string s_profileName = ResolveProfileName(Environment.GetCommandLineArgs());
    private static readonly bool s_isExplicit = IsExplicitlyRequested(Environment.GetCommandLineArgs());
    private static readonly Lazy<Abstractions.Composition.Profile.ResolvedProfile?> s_profile = new(Load);

    /// <summary>当前生效的 profile 名（用于报错措辞）。</summary>
    public static string ProfileName => s_profileName;

    /// <summary>当前解析出的 profile；读不到时为 <see langword="null"/>（= 全开）。</summary>
    public static Abstractions.Composition.Profile.ResolvedProfile? Current => s_profile.Value;

    /// <summary>
    /// 只在「用户显式指定了 profile」时返回解析结果，否则 <see langword="null"/>。
    /// <para>
    /// 给「宿主里已经有一个 IProfileManager，但它不知道命令行说了什么」的场景用：
    /// <c>prompt</c> 这类自省命令走轻量宿主，其中的 <see cref="Abstractions.Composition.IProfileManager"/>
    /// 是用默认名 <c>base</c> 构造的，直接读它会让 <c>jcc --profile ci prompt</c> 打印出 base 的提示词。
    /// 用 <see cref="Current"/> 又会在「运行期 /profile 切换」后给出过期答案 —— 所以用「是否显式指定」
    /// 来选择信源，而不是让两个信源互相覆盖。
    /// </para>
    /// </summary>
    public static Abstractions.Composition.Profile.ResolvedProfile? CurrentExplicit => s_isExplicit ? s_profile.Value : null;

    /// <summary>
    /// 判断某消费点下的某节点在当前 profile 是否启用。
    /// </summary>
    /// <param name="consumerId">消费点 ID（如 <c>tool-groups</c>）。</param>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>启用返回 <see langword="true"/>；profile 不可用时视为全开。</returns>
    public static bool IsEnabled(string consumerId, string nodeId) =>
        Abstractions.Composition.Profile.ConsumerDeclaration.IsEnabled(s_profile.Value, consumerId, nodeId);

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

    /// <summary>命令行或环境变量里是否明确指定了 profile（与「解析结果恰好是 base」区分开）。</summary>
    /// <param name="args">进程参数（首元素为可执行文件路径）。</param>
    /// <returns>明确指定返回 <see langword="true"/>。</returns>
    private static bool IsExplicitlyRequested(string[] args) {
        var flag = JccCliArgEnumConstants.Profile;
        var prefix = flag + "=";
        foreach (var arg in args) {
            if (string.Equals(arg, flag, StringComparison.Ordinal) || arg.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JccEnvVar.NodeProfile.ToValue()));
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
            Diag.WriteLine($"[CLI-GATE] profile '{s_profileName}' 读取失败，门禁按全开处理: {ex.Message}");
            return null;
        }
    }
}
