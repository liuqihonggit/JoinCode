namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 设置键 → 文件内 JSON 位置的唯一映射表 —— 与 <see cref="SettingSourceMap"/> 配套：
/// 后者回答「这个键住哪个文件」，本表回答「在这个文件里的哪个字段」。
/// <para><b>为什么需要它</b>：<c>config_get</c> / <c>config_set</c> 原本一律按扁平键名读写
/// <c>settings.json</c> 的 <c>current.&lt;key&gt;</c>，但有三个键的真身不在那儿：
/// <c>model</c> 在 <c>vendor.{当前供应商}.model</c>，<c>permissions.defaultMode</c> 在
/// <c>current.permissions.defaultMode</c>。后者曾在 <c>ConfigSetting.Path</c> 里声明过，
/// 但那个字段<b>零消费方</b> —— 写了没人读，于是 <c>config_set permissions.defaultMode</c>
/// 静默写进扁平键、运行时读的却是嵌套键，用户改权限模式不生效。</para>
/// <para><b>表外键</b>一律按扁平键处理（<c>current.&lt;key&gt;</c>），与既有行为一致。</para>
/// <para><b>为什么在契约层</b>：工具 exe 受依赖墙只能引契约层；规则留在上层就必然再出现第二套
/// （与 <see cref="SettingSourceMap"/>、参数名归一化同一条理由）。</para>
/// </summary>
public static class SettingPathMap {
    /// <summary>当前供应商占位符 —— 运行时替换为 <c>settings.json</c> 的 <c>current.profile</c>。</summary>
    public const string ProfilePlaceholder = "{profile}";

    /// <summary>
    /// 键 → JSON 路径段。
    /// <para>路径<b>从文件根开始</b>写全：<c>model</c> 的 <c>vendor.{profile}.model</c> 不在 <c>current</c> 下，
    /// <c>permissions.defaultMode</c> 在 <c>current</c> 下 —— 都写全才不会让消费方再猜一次前缀。</para>
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> Paths =
        new Dictionary<string, string[]>(StringComparer.Ordinal) {
            // 生效模型 = 运行时真正读取的字段（EngineApplicationBuilder: settings.GetActiveProfile()?.Model）
            ["model"] = ["vendor", ProfilePlaceholder, "model"],
            // 默认权限模式 = PermissionChecker 读的 settings.Current.Permissions.DefaultMode
            ["permissions.defaultMode"] = ["current", "permissions", "defaultMode"],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// 取设置键的 JSON 路径；表外键返回 <c>null</c>（表示扁平键 <c>current.&lt;key&gt;</c>）。
    /// </summary>
    /// <param name="key">设置键名（存储键，非枚举名）。</param>
    /// <returns>路径段数组，或 <c>null</c>。</returns>
    public static string[]? PathOf(string key) =>
        key is not null && Paths.TryGetValue(key, out var path) ? path : null;

    /// <summary>该键是否声明了嵌套路径（需要按路径读写而非扁平键）。</summary>
    /// <param name="key">设置键名。</param>
    /// <returns>声明了返回 true。</returns>
    public static bool IsNested(string key) => key is not null && Paths.ContainsKey(key);

    /// <summary>全部已声明路径的键 — 供对账脚本与文档核对。</summary>
    public static IReadOnlyDictionary<string, string[]> All => Paths;
}
