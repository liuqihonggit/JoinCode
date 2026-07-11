namespace JoinCode.ChatCommands;

/// <summary>
/// /install 命令 — 对齐 TS install.tsx
/// TS 类型为 prompt，将安装请求发送给 AI 执行
/// 对齐内容：将安装任务描述发送给 ChatService
/// 架构差异：TS 有 React 交互式确认，C# 为直接发送
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Install, Description = "让 AI 执行安装任务", Usage = "/install <package-or-command>", Category = ChatCommandCategory.Tools)]
public sealed class InstallCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Install;
    public string Description => "让 AI 执行安装任务";
    public string Usage => "/install <package-or-command>";
    public string[] Aliases => [];
    public string ArgumentHint => "<package-or-command>";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var target = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(target))
        {
            // 无参数时：StepFlow 引导选择安装目标
            var flow = new StepFlow(
            [
                new Step("选择安装类型", "请选择要安装的类型:\n\n  1. 系统工具 (nodejs, python, docker, git...)\n  2. NuGet 包\n  3. npm 包\n  4. 其他\n\n使用 /install <名称> 直接指定安装目标"),
                new Step("安装说明", "AI 将自动检测操作系统并使用合适的包管理器完成安装。\n\n示例:\n  /install nodejs\n  /install python\n  /install docker\n  /install <nuget-package-name>\n\n安装完成后 AI 会验证安装是否成功。"),
            ]);

            await flow.ShowAsync(context.CancellationToken).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 有参数时：StepFlow 确认 + 执行
        var installFlow = new StepFlow(
        [
            new Step("确认安装", $"即将安装: {TerminalColors.Accent}{target}{AnsiStyleConstants.Reset}\n\nAI 将自动检测操作系统并使用合适的包管理器。\n按 Enter 确认开始安装。"),
            new Step("安装中", $"{TerminalColors.Muted}正在请求 AI 安装: {target}{AnsiStyleConstants.Reset}\n\n请等待 AI 完成安装和验证..."),
        ]);

        var stepResult = await installFlow.ShowAsync(context.CancellationToken).ConfigureAwait(false);

        if (stepResult < 0)
        {
            TerminalHelper.WriteLine("安装已取消");
            return ChatCommandResult.Continue();
        }

        var prompt = $"Please install {target} on this system. Detect the operating system and use the appropriate package manager. Verify the installation was successful after completing it.";

        try
        {
            var result = await context.Services!.ChatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(result);
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("安装任务已取消");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("安装任务", ex);
        }

        return ChatCommandResult.Continue();
    }
}
