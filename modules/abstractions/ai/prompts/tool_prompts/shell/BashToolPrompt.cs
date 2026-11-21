namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// Bash工具提示词 — 对齐 TS BashTool prompt.ts
/// <para>沙箱措辞随配置动态生成（对齐 <c>prompt.ts:228-256</c>）：
/// 默认留沙箱 / 何时允许跳出 / 策略禁止跳出的三段式。</para>
/// </summary>
[ToolPrompt(ToolName = ShellToolName.Bash, Category = ToolPromptCategory.Shell)]
public static class BashToolPrompt {
    private static volatile bool _sandboxEnabled;
    private static volatile bool _allowUnsandboxedCommands = true;

    /// <summary>
    /// 注入沙箱策略快照 — 启动期由组合层调用一次；配置热更新后需再次调用。
    /// </summary>
    /// <param name="enabled">是否启用命令沙箱。</param>
    /// <param name="allowUnsandboxedCommands">是否允许 <c>dangerously_disable_sandbox</c> 跳出沙箱。</param>
    public static void SetSandboxPolicy(bool enabled, bool allowUnsandboxedCommands) {
        _sandboxEnabled = enabled;
        _allowUnsandboxedCommands = allowUnsandboxedCommands;
    }

    /// <summary>获取 Bash 工具描述文本。</summary>
    public static string GetDescription() => $"""
        执行 shell 命令。工作目录在命令之间持久化；shell 状态（变量、函数）不会。

        重要：此工具用于通过 Bash 进行终端操作：git、npm、docker 等。不要将其用于文件操作（读取、写入、编辑、搜索、查找文件）— 请改用专门的工具。

        使用方法：
        - command 参数是要执行的 shell 命令（必需）
        - description 参数简要描述命令用途（可选，推荐填写）
        - timeout 参数是命令超时时间，以毫秒为单位（可选，默认 120000ms）
        - working_directory 参数是命令执行的工作目录（可选，默认为当前目录）
        - run_in_background 参数设置命令在后台运行（可选）
        - dangerously_disable_sandbox 参数覆盖沙箱模式（可选）
        {BuildSandboxSection()}
        在执行命令之前，请遵循以下步骤：

        1. 目录验证：
           - 如果命令将创建新目录或文件，首先使用 ls 验证父目录存在且是正确的位置

        2. 命令执行：
           - 始终用双引号引用包含空格的文件路径
           - 捕获命令的输出

        使用说明：
          - 如果输出超过 30000 个字符，完整输出将保存到磁盘文件，你将收到预览和文件路径 — 使用 {FileToolNameEnumConstants.FileRead} 工具读取完整输出，不要重新执行命令
          - 当发出多个命令时：
            - 如果命令是独立的且可以并行运行，在单条消息中进行多个 Bash 工具调用
            - 如果命令相互依赖且必须顺序运行，使用 && 链式执行
          - 不要用 cd 前缀命令 — 工作目录已经自动设置为正确的项目目录

        对于 git 命令：
          - 优先创建新提交而不是修改现有提交
          - 在运行破坏性操作之前，考虑是否有更安全的替代方案
          - 永远不要跳过钩子（--no-verify）或绕过签名（--no-gpg-sign），除非用户明确要求
          - 禁用分页器：git --no-pager log，git --no-pager diff（避免交互式分页器卡住）
        """;

    /// <summary>按沙箱策略生成措辞段 — 未启用沙箱时为空。</summary>
    private static string BuildSandboxSection() {
        if (!_sandboxEnabled) {
            return string.Empty;
        }

        return _allowUnsandboxedCommands
            ? """

            沙箱模式：
              - 默认在沙箱内执行所有命令。除非满足以下条件，否则不要设置 dangerously_disable_sandbox=true：
                - 用户明确要求绕过沙箱
                - 某条命令刚刚失败，且证据表明是沙箱限制导致（如 Operation not permitted、访问白名单外路径被拒、连接非白名单域名失败、Unix socket 连接错误）
              - 看到沙箱导致的失败证据时：立即用 dangerously_disable_sandbox=true 重试，并简要说明是哪条限制造成的（用户可用 /sandbox 管理限制）。每次跳出都单独授权，后续命令仍默认留在沙箱。
              - 临时文件一律使用 $TMPDIR（沙箱模式下自动指向可写目录），不要直接使用 /tmp。
              - 不要建议把 ~/.bashrc、~/.zshrc、~/.ssh/* 或凭据文件加入沙箱允许列表。

            """
            : """

            沙箱模式：
              - 所有命令必须在沙箱内执行：dangerously_disable_sandbox 参数已被策略禁用，任何情况下都不能跳出沙箱。
              - 如果命令因沙箱限制失败，请与用户一起通过 /sandbox 调整沙箱配置，不要尝试绕过。
              - 临时文件一律使用 $TMPDIR（沙箱模式下自动指向可写目录），不要直接使用 /tmp。

            """;
    }
}
