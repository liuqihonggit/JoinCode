namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// EnterWorktreeTool 提示词
/// </summary>
[ToolPrompt(ToolName = "EnterWorktree", Category = ToolPromptCategory.Agent)]
public static class EnterWorktreeToolPrompt
{
    public const string ToolName = "EnterWorktree";

    public static string Prompt { get; } = $"""
        仅当用户明确要求在工作区中工作时使用此工具。此工具创建一个隔离的 git 工作区并将当前会话切换到其中。

        ## 何时使用

        - 用户明确说"工作区"（例如，"启动工作区"、"在工作区中工作"、"创建工作区"、"使用工作区"）

        ## 何时不使用

        - 用户要求创建分支、切换分支或在不同分支上工作 —— 改用 git 命令
        - 用户要求修复错误或处理功能 —— 除非他们特别提到工作区，否则使用正常的 git 工作流
        - 永远不要使用此工具，除非用户明确提到"工作区"

        ## 要求

        - 必须在 git 仓库中，或者在 settings.json 中配置了 WorktreeCreate/WorktreeRemove 钩子
        - 必须尚未在工作区中

        ## 行为

        - 在 git 仓库中：在 `{AppDataConstants.AppDataFolder}/{AppDataConstants.WorktreesFolderName}/` 内创建一个新的 git 工作区，并基于 HEAD 创建一个新分支
        - 在 git 仓库外：委托给 WorktreeCreate/WorktreeRemove 钩子进行 VCS 无关的隔离
        - 将会话的工作目录切换到新工作区
        - 使用 ExitWorktree 在会话中途离开工作区（保留或删除）。在会话退出时，如果仍在工作区中，用户将被提示保留或删除它

        ## 参数

        - `name`（可选）：工作区的名称。如果未提供，则生成一个随机名称。
        """;
}

/// <summary>
/// ExitWorktreeTool 提示词
/// </summary>
[ToolPrompt(ToolName = "ExitWorktree", Category = ToolPromptCategory.Agent)]
public static class ExitWorktreeToolPrompt
{
    public const string ToolName = WorktreeToolNameConstants.ExitWorktree;

    public const string Prompt = """
        退出由 EnterWorktree 创建的工作区会话，并将会话返回到原始工作目录。

        ## 范围

        此工具仅对此会话中由 EnterWorktree 创建的工作区进行操作。它不会触及：
        - 你手动使用 `git worktree add` 创建的工作区
        - 来自前一个会话的工作区（即使当时由 EnterWorktree 创建）
        - 如果从未调用 EnterWorktree，则不会触及你所在的目录

        如果在 EnterWorktree 会话之外调用，该工具是**无操作**：它报告没有活动的工作区会话，不采取任何操作。文件系统状态不变。

        ## 何时使用

        - 用户明确要求"退出工作区"、"离开工作区"、"返回"或以其他方式结束工作区会话
        - 不要主动调用此工具 —— 仅在用户要求时调用

        ## 参数

        - `action`（必需）：`"keep"` 或 `"remove"`
          - `"keep"` —— 保留工作区目录和分支在磁盘上。如果用户想稍后回来工作，或如果有更改要保留，请使用此选项。
          - `"remove"` —— 删除工作区目录及其分支。当工作完成或放弃时，使用此选项进行干净的退出。
        - `discard_changes`（可选，默认 false）：仅对 `action: "remove"` 有意义。如果工作区有未提交的文件或原始分支上没有的提交，除非将此设置为 `true`，否则工具将拒绝删除它。如果工具返回列出更改的错误，在重新调用并设置 `discard_changes: true` 之前与用户确认。

        ## 行为

        - 将会话的工作目录恢复到 EnterWorktree 之前的位置
        - 清除依赖于 CWD 的缓存（系统提示部分、记忆文件、计划目录），以便会话状态反映原始目录
        - 如果 tmux 会话附加到工作区：在 `remove` 时终止，在 `keep` 时保持运行（返回其名称以便用户可以重新附加）
        - 一旦退出，可以再次调用 EnterWorktree 以创建一个新的工作区
        """;
}
