namespace Tools.Handlers;


/// <summary>
/// 异步(后台)Agent 允许的工具集 — 限制后台 agent 不能交互提问、不能停止其他任务
/// <para>对齐 TS 原版 ASYNC_AGENT_ALLOWED_TOOLS</para>
/// <para>排除: AskUser/TaskStop/TaskOutput/EnterPlanMode/ExitPlanMode/Agent(递归)/Workflow</para>
/// </summary>
public static class AsyncAgentAllowedTools
{
    /// <summary>后台 Agent 允许的工具 FrozenSet — O(1) 查找, AOT 友好</summary>
    public static readonly FrozenSet<string> Tools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        FileToolNameConstants.FileRead,
        FileToolNameConstants.FileWrite,
        FileToolNameConstants.FileEdit,
        SearchToolNameConstants.Glob,
        SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCodebase,
        ShellToolNameConstants.Bash,
        NotebookToolNameConstants.NotebookEdit,
        "web_fetch",
        "web_search",
        "todo_write",
        "skill",
        "tool_search",
        "enter_worktree",
        "exit_worktree",
        "read",
        "write",
        "edit",
        "glob",
        "grep",
        "bash"
    );
}
