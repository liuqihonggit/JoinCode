namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局对象类型 — 每种实体类型对应一个枚举值
/// ObjectId = ObjectType + 领域ID，跨域引用'一引用即定位'
/// </summary>
public enum ObjectType
{
    [EnumValue("agent")] Agent,
    [EnumValue("session")] Session,
    [EnumValue("goal")] Goal,
    [EnumValue("task")] Task,
    [EnumValue("request")] Request,
    [EnumValue("tool")] Tool,
    [EnumValue("mcp")] Mcp,
    [EnumValue("plan")] Plan,
    [EnumValue("team")] Team,
    [EnumValue("cron")] Cron,
    [EnumValue("build")] Build,
    [EnumValue("sandbox")] Sandbox,
    [EnumValue("repo")] Repo,
    [EnumValue("notification")] Notification,
    [EnumValue("worktree")] Worktree,
    [EnumValue("bash")] Bash,
}
