namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局对象类型 — 每种实体类型对应一个枚举值
/// ObjectId = ObjectType + 领域ID，跨域引用'一引用即定位'
/// None = 0 是 ObjectId.Empty 的类型标记，保证 default(ObjectId) == ObjectId.Empty
/// </summary>
public enum ObjectType
{
    [EnumValue("none")] None = 0,
    [EnumValue("agent")] Agent = 1,
    [EnumValue("session")] Session = 2,
    [EnumValue("goal")] Goal = 3,
    [EnumValue("task")] Task = 4,
    [EnumValue("request")] Request = 5,
    [EnumValue("tool")] Tool = 6,
    [EnumValue("mcp")] Mcp = 7,
    [EnumValue("plan")] Plan = 8,
    [EnumValue("team")] Team = 9,
    [EnumValue("cron")] Cron = 10,
    [EnumValue("build")] Build = 11,
    [EnumValue("sandbox")] Sandbox = 12,
    [EnumValue("repo")] Repo = 13,
    [EnumValue("notification")] Notification = 14,
    [EnumValue("worktree")] Worktree = 15,
    [EnumValue("shellcommand")] ShellCommand = 16,
    [EnumValue("executor")] Executor = 17,
    [EnumValue("service")] Service = 18,
}
