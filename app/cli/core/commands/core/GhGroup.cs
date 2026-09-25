namespace JoinCode.CliCommands;

/// <summary>
/// gh CLI 分组 — 与 gh_* 工具前缀一一对应
/// </summary>
internal enum GhGroup {
    [EnumValue("pr")] Pr,
    [EnumValue("issue")] Issue,
    [EnumValue("repo")] Repo,
    [EnumValue("release")] Release,
    [EnumValue("run")] Run,
    [EnumValue("branch")] Branch,
    [EnumValue("api")] Api,
}
