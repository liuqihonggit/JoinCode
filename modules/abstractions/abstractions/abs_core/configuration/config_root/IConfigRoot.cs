
namespace JoinCode.Abstractions.Configuration.ConfigRoot;

/// <summary>
/// 统一配置入口 — 按字段路径获取配置，IMemoryCache 懒加载，内存压力自动释放（TASK054）。
/// <para>线程安全：Get 返回不可变快照，调用方直接 = 赋值即安全，配置变更原子替换不影已持有快照。</para>
/// <para>三层合并：Local &gt; Pro &gt; User，第一个非 null 返回。</para>
/// </summary>
public interface IConfigRoot {
    /// <summary>用户级配置层（~/.jcc/，跨项目共享，基础）</summary>
    IConfigLayer User { get; }

    /// <summary>项目级配置层（{projectRoot}/.jcc/，项目隔离，覆盖 User）</summary>
    IConfigLayer Pro { get; }

    /// <summary>本地配置层（{projectRoot}/.jcc/settings.local.json，gitignore，覆盖 Pro）</summary>
    IConfigLayer Local { get; }

    /// <summary>
    /// 按路径获取配置值 — 合并 Local &gt; Pro &gt; User，第一个非 null 返回。
    /// <para>路径格式：点分，如 "current.ui.theme"、"vendors.openai.endpoint"。</para>
    /// <para>缓存未命中时自动从物理文件提取并缓存（IMemoryCache 两级缓存）。</para>
    /// <para>返回不可变快照，直接 = 赋值线程安全。</para>
    /// </summary>
    /// <typeparam name="T">配置值类型（必须不可变：record 或 init-only）</typeparam>
    /// <param name="path">点分配置路径</param>
    /// <returns>命中返回不可变快照；全层未命中返回 default</returns>
    T? Get<T>(string path);

    /// <summary>
    /// 按路径获取配置值，全层未命中返回 <paramref name="defaultValue"/>。
    /// </summary>
    T Get<T>(string path, T defaultValue);

    /// <summary>
    /// 获取配置子节 — 返回可继续 Get 的子节视图。
    /// <para>如 GetSection("current.ui") 后可 .Get&lt;string&gt;("theme")。</para>
    /// </summary>
    IConfigSection GetSection(string path);

    /// <summary>
    /// 使指定路径缓存失效 — 配置文件变更时调用，触发下次 Get 重新从物理文件提取。
    /// <para>path 为 null 或空时清空全部缓存。</para>
    /// </summary>
    void Invalidate(string path);
}

/// <summary>
/// 单层配置访问 — User / Pro / Local 各层独立访问，不跨层合并。
/// <para>线程安全：Get 返回不可变快照。</para>
/// </summary>
public interface IConfigLayer {
    /// <summary>该层配置是否存在（对应物理目录/文件是否存在）</summary>
    bool Exists { get; }

    /// <summary>
    /// 仅在本层按路径获取配置值。
    /// <para>缓存未命中时自动从本层物理文件提取并缓存。</para>
    /// <para>返回不可变快照，直接 = 赋值线程安全。</para>
    /// </summary>
    T? Get<T>(string path);
}

/// <summary>
/// 配置子节视图 — 从某个路径前缀下继续按相对路径获取，避免重复写长路径前缀。
/// <para>线程安全：Get 返回不可变快照。</para>
/// </summary>
public interface IConfigSection {
    /// <summary>
    /// 在本子节下按相对路径获取配置值。
    /// <para>如 section = GetSection("current.ui")，section.Get&lt;string&gt;("theme") 等价 Get&lt;string&gt;("current.ui.theme")。</para>
    /// </summary>
    T? Get<T>(string relativePath);

    /// <summary>在本子节下获取更深层子节。</summary>
    IConfigSection GetSection(string relativePath);
}

/// <summary>
/// 项目根定位器 — 从当前工作目录向上搜索 .git/ 或 .jcc/ 定位项目根，用于 Pro/Local 层项目隔离。
/// <para>找不到时返回 null（仅 User 层可用）。</para>
/// </summary>
public interface IProjectLocator {
    /// <summary>
    /// 定位当前项目根目录绝对路径。
    /// <para>搜索策略：从 CWD 向上找 .git/ 目录（git 根）或 .jcc/ 目录，取最近的一个。</para>
    /// <para>找不到返回 null — 此时 Pro/Local 层不存在，仅 User 层可用。</para>
    /// </summary>
    string? LocateProjectRoot();
}
