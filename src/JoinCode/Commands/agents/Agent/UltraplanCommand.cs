namespace JoinCode.ChatCommands;

/// <summary>
/// /ultraplan 命令 — 对齐 TS ultraplan.tsx
/// TS 使用深度规划模式，多步骤规划+执行
/// 对齐内容：深度规划+步骤执行+进度跟踪
/// 架构差异：TS 有 React 交互式计划面板，C# 为命令行操作
/// 待办：需要 PlanService 扩展支持多步骤执行器
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Ultraplan, Description = "超级计划模式：深度规划+执行", Usage = "/ultraplan [goal] [--steps N] [--execute]", Category = ChatCommandCategory.Agent, Aliases = ["up"], ArgumentHint = "[goal]")]
public sealed class UltraplanCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);

        if (args.Length == 0)
        {
            ShowHelp();
            return ChatCommandResult.Continue();
        }

        var goal = string.Empty;
        var steps = 5;
        var autoExecute = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--steps", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg["--steps".Length..].TrimStart('='), out var s))
            {
                steps = s;
            }
            else if (arg is "--execute" or "-e")
            {
                autoExecute = true;
            }
            else if (!arg.StartsWith("-"))
            {
                goal = string.IsNullOrEmpty(goal) ? arg : $"{goal} {arg}";
            }
        }

        if (string.IsNullOrEmpty(goal))
        {
            ShowHelp();
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine($"{TerminalColors.Primary}=== 超级计划模式 ==={AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"目标: {goal}");
        TerminalHelper.WriteLine($"最大步骤: {steps}");
        TerminalHelper.WriteLine($"自动执行: {(autoExecute ? "是" : "否")}");
        TerminalHelper.NewLine();

        try
        {
            if (autoExecute)
            {
                // --execute: 复用 PlanService.ExecutePlanWithResultAsync（已配置 ToolChoice.AutoInvoke）
                // LLM 会自动调用工具执行步骤，无需在命令层重复实现执行器
                await ExecutePlanViaPlanServiceAsync(context, goal).ConfigureAwait(false);
            }
            else
            {
                // 默认: 仅生成并展示计划文本
                var prompt = $"Create a detailed step-by-step plan (maximum {steps} steps) to accomplish the following goal. For each step, specify: 1) What to do, 2) Why it's needed, 3) How to verify it succeeded.\n\nGoal: {goal}";

                var result = await context.Services.ChatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine(result);
            }
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("计划已取消");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("超级计划", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ExecutePlanViaPlanServiceAsync(ChatCommandContext context, string goal)
    {
        var planService = context.Services.PlanService;
        var executionResult = await planService.ExecutePlanWithResultAsync(goal, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.NewLine();
        if (executionResult.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}=== 计划执行完成 ==={AnsiStyleConstants.Reset}");
            if (!string.IsNullOrEmpty(executionResult.Result))
            {
                TerminalHelper.WriteLine(executionResult.Result);
            }
            TerminalHelper.WriteLine($"{TerminalColors.Secondary}耗时: {executionResult.ExecutionTimeMs}ms{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}=== 计划执行失败 ==={AnsiStyleConstants.Reset}");
            if (!string.IsNullOrEmpty(executionResult.Error))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}{executionResult.Error}{AnsiStyleConstants.Reset}");
            }
        }
    }

    private static void ShowHelp()
    {
        TerminalHelper.WriteLine("=== 超级计划模式 ===\n");
        TerminalHelper.WriteLine("用法: /ultraplan <goal> [--steps N] [--execute]");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("深度规划+执行模式，AI 将:");
        TerminalHelper.WriteLine("  1. 分析目标并拆解为步骤");
        TerminalHelper.WriteLine("  2. 为每个步骤制定详细计划");
        TerminalHelper.WriteLine("  3. 逐步执行并验证结果");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("选项:");
        TerminalHelper.WriteLine("  --steps N    最大步骤数 (默认: 5)");
        TerminalHelper.WriteLine("  --execute    自动执行计划步骤");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("示例:");
        TerminalHelper.WriteLine("  /ultraplan 重构用户认证模块");
        TerminalHelper.WriteLine("  /ultraplan 添加单元测试 --steps 10");
        TerminalHelper.WriteLine("  /ultraplan 修复所有编译错误 --execute");
    }
}
