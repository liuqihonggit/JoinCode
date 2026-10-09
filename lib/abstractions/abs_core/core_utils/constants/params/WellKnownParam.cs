namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 公共参数名称枚举 — 消除 869 处手写参数描述重复的唯一数据源。
/// <para>每个枚举成员用 [EnumValue] 绑定参数名，[ParamMeta] 绑定描述/必填/类型。</para>
/// <para>源码生成器生成 WellKnownParamConstants（参数名常量）+ WellKnownParamDescriptions（描述常量）。</para>
/// <para>使用方式：[McpToolParameter(WellKnownParam.WorkingDir)] 替代 [McpToolParameter("工作目录(可选)", Required = false)]。</para>
/// </summary>
public enum WellKnownParam {
    // === 通用参数 ===

    [EnumValue("working_dir")]
    [ParamMeta("工作目录(可选)", Required = false, TypeName = "string")]
    WorkingDir,

    [EnumValue("repo")]
    [ParamMeta("仓库(可选,默认当前仓库)", Required = false, TypeName = "string")]
    Repo,

    [EnumValue("verbosity")]
    [ParamMeta("输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON)", Required = false, DefaultValue = "0", TypeName = "int")]
    Verbosity,

    [EnumValue("limit")]
    [ParamMeta("数量限制(默认 30)", Required = false, DefaultValue = "30", TypeName = "int")]
    Limit,

    [EnumValue("json_fields")]
    [ParamMeta("JSON 字段过滤(可选,逗号分隔)", Required = false, TypeName = "string")]
    JsonFields,

    // === GitHub 实体标识 ===

    [EnumValue("pr_number")]
    [ParamMeta("PR 编号或 URL", Required = true, TypeName = "string")]
    PrNumber,

    [EnumValue("issue_number")]
    [ParamMeta("Issue 编号或 URL", Required = true, TypeName = "string")]
    IssueNumber,

    [EnumValue("run_id")]
    [ParamMeta("Run ID", Required = true, TypeName = "string")]
    RunId,

    [EnumValue("workflow_id")]
    [ParamMeta("Workflow ID 或文件名", Required = true, TypeName = "string")]
    WorkflowId,

    [EnumValue("job_id")]
    [ParamMeta("Job ID", Required = true, TypeName = "string")]
    JobId,

    [EnumValue("branch")]
    [ParamMeta("分支名", Required = true, TypeName = "string")]
    Branch,

    [EnumValue("owner")]
    [ParamMeta("仓库所有者(owner)", Required = true, TypeName = "string")]
    Owner,

    [EnumValue("tag")]
    [ParamMeta("标签名(tag)", Required = true, TypeName = "string")]
    Tag,

    [EnumValue("release_id")]
    [ParamMeta("Release ID", Required = true, TypeName = "string")]
    ReleaseId,

    [EnumValue("label")]
    [ParamMeta("标签名", Required = true, TypeName = "string")]
    Label,

    [EnumValue("milestone_number")]
    [ParamMeta("里程碑编号", Required = true, TypeName = "string")]
    MilestoneNumber,

    [EnumValue("project_number")]
    [ParamMeta("项目编号", Required = true, TypeName = "string")]
    ProjectNumber,

    [EnumValue("discussion_number")]
    [ParamMeta("讨论编号", Required = true, TypeName = "string")]
    DiscussionNumber,

    // === GitHub 过滤/排序参数 ===

    [EnumValue("state")]
    [ParamMeta("状态过滤(open/closed/all,默认 open)", Required = false, DefaultValue = "open", TypeName = "string")]
    State,

    [EnumValue("sort")]
    [ParamMeta("排序字段", Required = false, TypeName = "string")]
    Sort,

    [EnumValue("direction")]
    [ParamMeta("排序方向(asc/desc,默认 desc)", Required = false, DefaultValue = "desc", TypeName = "string")]
    Direction,

    [EnumValue("filter")]
    [ParamMeta("过滤条件", Required = false, TypeName = "string")]
    Filter,

    // === GitHub 变更参数 ===

    [EnumValue("title")]
    [ParamMeta("标题", Required = false, TypeName = "string")]
    Title,

    [EnumValue("body")]
    [ParamMeta("正文内容", Required = false, TypeName = "string")]
    Body,

    [EnumValue("base")]
    [ParamMeta("目标分支(base)", Required = false, TypeName = "string")]
    BaseBranch,

    [EnumValue("head")]
    [ParamMeta("源分支(head)", Required = false, TypeName = "string")]
    HeadBranch,

    // === 输出控制参数 ===

    [EnumValue("comments")]
    [ParamMeta("是否附带评论列表", Required = false, TypeName = "boolean")]
    Comments,

    [EnumValue("web")]
    [ParamMeta("只返回浏览器 URL", Required = false, TypeName = "boolean")]
    Web,

    [EnumValue("draft")]
    [ParamMeta("是否为草稿", Required = false, TypeName = "boolean")]
    Draft,

    // === MCP 协议参数 ===

    [EnumValue("tool_name")]
    [ParamMeta("工具名称", Required = true, TypeName = "string")]
    ToolName,

    [EnumValue("args")]
    [ParamMeta("工具参数(JSON 对象)", Required = false, TypeName = "object")]
    Args,

    [EnumValue("timeout")]
    [ParamMeta("超时秒数", Required = false, DefaultValue = "30", TypeName = "int")]
    Timeout,
}
