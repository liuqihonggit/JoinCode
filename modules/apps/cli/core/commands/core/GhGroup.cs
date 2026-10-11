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
    [EnumValue("label")] Label,
    [EnumValue("search")] Search,
    [EnumValue("workflow")] Workflow,
    [EnumValue("auth")] Auth,
    [EnumValue("config")] Config,
    [EnumValue("gist")] Gist,
    [EnumValue("org")] Org,
    [EnumValue("ssh-key")] SshKey,
    [EnumValue("gpg-key")] GpgKey,
    [EnumValue("secret")] Secret,
    [EnumValue("variable")] Variable,
    [EnumValue("cache")] Cache,
    [EnumValue("ruleset")] Ruleset,
    [EnumValue("codespace")] Codespace,
    [EnumValue("discussion")] Discussion,
    [EnumValue("project")] Project,
    [EnumValue("alias")] Alias,
    [EnumValue("extension")] Extension,
    [EnumValue("browse")] Browse,
    [EnumValue("status")] Status,
    [EnumValue("licenses")] Licenses,
}
