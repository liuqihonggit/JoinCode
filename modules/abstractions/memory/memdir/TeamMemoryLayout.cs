namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 团队记忆目录布局 — 唯一真相源。
/// 目录形如 <c>&lt;memdir&gt;/team-memories/teams/&lt;teamId&gt;/{shared|members/&lt;userId&gt;}</c>。
/// 路径服务、写入前密钥门禁、同步都从这里取根，禁止各自再写一份 "team-memories" 字面量。
/// </summary>
public static class TeamMemoryLayout {
    /// <summary>团队记忆根目录名（位于 memdir 存储根之下）。</summary>
    public const string RootDirectoryName = "team-memories";

    /// <summary>按 memdir 存储根解析团队记忆根目录。</summary>
    /// <param name="memdirStoragePath">memdir 存储根（调用方负责给默认值，避免本层依赖配置常量）</param>
    public static string ResolveRoot(string memdirStoragePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(memdirStoragePath);
        return Path.Combine(memdirStoragePath, RootDirectoryName);
    }
}
