namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// Bash工具提示词 — 对齐 TS BashTool prompt.ts
/// </summary>
[ToolPrompt(ToolName = "Bash", Category = ToolPromptCategory.Shell)]
public static class BashToolPrompt
{
    public static string GetDescription() => """
        执行 shell 命令。工作目录在命令之间持久化；shell 状态（变量、函数）不会。

        重要：此工具用于通过 Bash 进行终端操作：git、npm、docker 等。不要将其用于文件操作（读取、写入、编辑、搜索、查找文件）— 请改用专门的工具。

        使用方法：
        - command 参数是要执行的 shell 命令（必需）
        - description 参数简要描述命令用途（可选，推荐填写）
        - timeout 参数是命令超时时间，以毫秒为单位（可选，默认 120000ms）
        - working_directory 参数是命令执行的工作目录（可选，默认为当前目录）
        - run_in_background 参数设置命令在后台运行（可选）
        - dangerously_disable_sandbox 参数覆盖沙箱模式（可选）

        在执行命令之前，请遵循以下步骤：

        1. 目录验证：
           - 如果命令将创建新目录或文件，首先使用 ls 验证父目录存在且是正确的位置

        2. 命令执行：
           - 始终用双引号引用包含空格的文件路径
           - 捕获命令的输出

        使用说明：
          - 如果输出超过 30000 个字符，完整输出将保存到磁盘文件，你将收到预览和文件路径 — 使用 FileRead 工具读取完整输出，不要重新执行命令
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
}
