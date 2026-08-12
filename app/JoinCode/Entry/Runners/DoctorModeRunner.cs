namespace JoinCode.Entry;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 医生模式运行器 — jcc.exe --doctor 入口
/// 通过 IAgentRunner 执行 doctor Agent，复用 Agent 基础设施
/// </summary>
internal static class DoctorModeRunner
{
    internal static async Task<int> RunAsync(CommandLineOptions options, IServiceProvider services)
    {
        Cli.TerminalHelper.Init();
        Diag.WriteLifecycle("[DOCTOR] 自举医生模式启动");

        var runner = services.GetService<IAgentRunner>();
        if (runner is null)
            throw new InvalidOperationException("无法从 DI 容器解析 IAgentRunner。请确保 ClockModule 已注册。");

        var agentProvider = services.GetService<IAgentDefinitionProvider>();
        var doctorDef = agentProvider is not null
            ? await agentProvider.GetAgentDefinitionAsync(AgentRole.Executor, ExecutorVariant.Doctor).ConfigureAwait(false)
            : null;

        var objective = doctorDef is not null
            ? doctorDef.WhenToUse
            : "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch";

        var systemPrompt = doctorDef?.SystemPrompt;

        Diag.WriteLifecycle($"[DOCTOR] 目标: {objective}");

        try
        {
            var state = await runner.RunAsync(
                objective,
                systemPrompt: systemPrompt).ConfigureAwait(false);

            await runner.WaitForCompletionAsync().ConfigureAwait(false);

            var finalState = runner.CurrentState;
            var exitCode = finalState?.Status switch
            {
                GoalStatus.Achieved => (int)ExitCode.Success,
                GoalStatus.Unmet => (int)ExitCode.GeneralError,
                GoalStatus.BudgetLimited => (int)ExitCode.GeneralError,
                _ => (int)ExitCode.ConfigurationError
            };

            PrintGoalReport(finalState);
            return exitCode;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("已有目标正在运行"))
        {
            Diag.WriteLifecycle($"[DOCTOR] 目标引擎已被占用: {ex.Message}");
            return (int)ExitCode.ConfigurationError;
        }
        catch (Exception ex)
        {
            Diag.WriteLifecycle($"[DOCTOR] 运行异常: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                Diag.WriteLifecycle($"[DOCTOR] 内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return (int)ExitCode.ConfigurationError;
        }
    }

    /// <summary>
    /// 打印目标报告
    /// </summary>
    private static void PrintGoalReport(GoalState? state)
    {
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
        Cli.TerminalHelper.WriteLine("  医生报告（GoalEngine）");
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");

        if (state is not null)
        {
            Cli.TerminalHelper.WriteLine($"  目标 ID:     {state.GoalId}");
            Cli.TerminalHelper.WriteLine($"  目标:        {state.Objective}");
            Cli.TerminalHelper.WriteLine($"  状态:        {state.Status}");
            Cli.TerminalHelper.WriteLine($"  轮次:        {state.TurnsCompleted}");
            Cli.TerminalHelper.WriteLine($"  Token 消耗:  {state.TokensUsed}");
            Cli.TerminalHelper.WriteLine($"  耗时:        {state.Elapsed:hh\\:mm\\:ss}");

            if (state.LastEvaluation is not null)
            {
                Cli.TerminalHelper.WriteLine($"  评估结果:    {(state.LastEvaluation.IsCompleted ? "已完成" : "未完成")}");
                Cli.TerminalHelper.WriteLine($"  评估原因:    {state.LastEvaluation.Reason}");
            }
        }
        else
        {
            Cli.TerminalHelper.WriteLine("  无目标状态");
        }

        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
    }
}
