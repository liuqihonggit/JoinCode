namespace JoinCode.ChatCommands;

/// <summary>
/// /falv 命令 — 结构化推理（三权分立）
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Falv, Description = "结构化推理引擎（假定→验证→事实）", Usage = "/falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --continue [rounds|tokens|both|default] | /falv --budget | /falv --reset", Category = ChatCommandCategory.Law)]
public sealed class FalvCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Falv;
    public string Description => "结构化推理引擎（假定→验证→事实）";
    public string Usage => "/falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --continue [rounds|tokens|both|default] | /falv --budget | /falv --reset";
    public string[] Aliases => [];
    public string ArgumentHint => "<假定内容|--status|--judge|--evidence|--continue|--budget|--reset>";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args) || args is "-h" or "--help")
        {
            ShowHelp();
            return ChatCommandResult.Continue();
        }

        var engine = ChatCommandBase.GetService<IReasoningEngine>(context, typeof(IReasoningEngine));
        if (engine is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}推理引擎未初始化{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        if (args.StartsWith("--continue"))
        {
            var refillArg = args.Length > "--continue".Length ? args["--continue".Length..].Trim() : string.Empty;
            await ContinueReasoningAsync(engine, refillArg, context.CancellationToken).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        switch (args)
        {
            case "--status":
                ShowStatus(engine);
                break;
            case "--judge":
                await engine.RunAdversarialProcessAsync(context.CancellationToken).ConfigureAwait(false);
                ShowVerdicts(engine);
                ShowBudgetIfExhausted(engine);
                break;
            case "--evidence":
                ShowEvidence(engine);
                break;
            case "--budget":
                ShowBudget(engine);
                break;
            case "--reset":
                TerminalHelper.WriteLine("推理引擎已重置（请重新创建引擎实例）");
                break;
            default:
                await AddAssumptionAsync(engine, args, context.CancellationToken).ConfigureAwait(false);
                ShowBudgetIfExhausted(engine);
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task AddAssumptionAsync(IReasoningEngine engine, string content, CancellationToken ct)
    {
        var assumption = new DataItem
        {
            Content = content,
            State = DataState.Assumption,
            Source = "用户输入",
        };

        await engine.AddAssumptionsAsync([assumption], ct).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Primary}[假定]{AnsiStyleConstants.Reset} {content}");
    }

    private static async Task ContinueReasoningAsync(IReasoningEngine engine, string refillArg, CancellationToken ct)
    {
        var mode = ParseRefillMode(refillArg);
        var budget = engine.GetBudgetStatus();

        if (!budget.IsAnyExhausted)
        {
            TerminalHelper.WriteLine($"预算尚未耗尽 — 轮次:{budget.RoundsUsed}/{budget.RoundsBudget} token:{budget.TokensUsed}/{budget.TokensBudget}");
            TerminalHelper.WriteLine("仍可继续推理，输入 /falv --judge 即可");
            return;
        }

        var causeLabel = budget.ExhaustionCause switch
        {
            BudgetExhaustionCause.Rounds => "轮次预算耗尽",
            BudgetExhaustionCause.Tokens => "Token预算耗尽",
            BudgetExhaustionCause.Both => "轮次和Token预算均耗尽",
            _ => "未知原因",
        };

        TerminalHelper.WriteLine($"{TerminalColors.Warning}[预算耗尽]{AnsiStyleConstants.Reset} {causeLabel}");
        TerminalHelper.WriteLine($"续费方式: {mode}");
        TerminalHelper.WriteLine("继续推理中...");

        await engine.ContinueAsync(mode, ct: ct).ConfigureAwait(false);
        ShowVerdicts(engine);
        ShowBudgetIfExhausted(engine);
    }

    private static BudgetRefillMode ParseRefillMode(string arg)
    {
        return arg.ToLowerInvariant() switch
        {
            "rounds" => BudgetRefillMode.RoundsOnly,
            "tokens" => BudgetRefillMode.TokensOnly,
            "both" => BudgetRefillMode.Both,
            "default" or "" => BudgetRefillMode.Default,
            _ => BudgetRefillMode.Default,
        };
    }

    private static void ShowBudgetIfExhausted(IReasoningEngine engine)
    {
        var budget = engine.GetBudgetStatus();
        if (!budget.IsAnyExhausted) return;

        var causeLabel = budget.ExhaustionCause switch
        {
            BudgetExhaustionCause.Rounds => $"{TerminalColors.Warning}轮次预算耗尽{AnsiStyleConstants.Reset}",
            BudgetExhaustionCause.Tokens => $"{TerminalColors.Warning}Token预算耗尽{AnsiStyleConstants.Reset}",
            BudgetExhaustionCause.Both => $"{TerminalColors.Error}轮次和Token预算均耗尽{AnsiStyleConstants.Reset}",
            _ => string.Empty,
        };

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"⚠ {causeLabel}");
        TerminalHelper.WriteLine($"  轮次: {budget.RoundsUsed}/{budget.RoundsBudget}  Token: {budget.TokensUsed}/{budget.TokensBudget}");
        TerminalHelper.WriteLine("  使用 /falv --continue [rounds|tokens|both|default] 续费并继续推理");
    }

    private static void ShowStatus(IReasoningEngine engine)
    {
        var summary = engine.GetSummary();
        TerminalHelper.WriteLine("=== 推理引擎状态 ===");
        TerminalHelper.WriteLine($"  假定: {summary.TotalAssumptions}");
        TerminalHelper.WriteLine($"  已验证: {summary.TotalVerified}");
        TerminalHelper.WriteLine($"  事实: {summary.TotalFacts}");
        TerminalHelper.WriteLine($"  被驳斥: {summary.TotalRejected}");
        TerminalHelper.WriteLine($"  待补充: {summary.TotalPendingEvidence}");
        TerminalHelper.WriteLine($"  证据总数: {summary.TotalEvidence}");

        if (summary.LastRunAt.HasValue)
        {
            TerminalHelper.WriteLine($"  最近裁决: {summary.LastRunAt.Value:HH:mm:ss}");
        }

        TerminalHelper.WriteLine();
        ShowBudget(engine);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine("=== 所有数据项 ===");
        foreach (var item in engine.GetAllItems())
        {
            var stateLabel = item.State switch
            {
                DataState.Fact => $"{TerminalColors.Primary}事实{AnsiStyleConstants.Reset}",
                DataState.Verified => $"已验证",
                DataState.Rejected => $"{TerminalColors.Error}被驳斥{AnsiStyleConstants.Reset}",
                DataState.PendingEvidence => $"{TerminalColors.Warning}待补充{AnsiStyleConstants.Reset}",
                _ => $"假定",
            };
            TerminalHelper.WriteLine($"  [{stateLabel}] {item.Content} (置信度:{item.Confidence}%)");
        }
    }

    private static void ShowBudget(IReasoningEngine engine)
    {
        var budget = engine.GetBudgetStatus();
        TerminalHelper.WriteLine("=== 预算状态 ===");

        var roundsColor = budget.IsRoundsExhausted ? TerminalColors.Error : TerminalColors.Primary;
        var tokensColor = budget.IsTokensExhausted ? TerminalColors.Error : TerminalColors.Primary;

        TerminalHelper.WriteLine($"  轮次: {roundsColor}{budget.RoundsUsed}/{budget.RoundsBudget}{AnsiStyleConstants.Reset} (剩余 {budget.RoundsRemaining})");
        TerminalHelper.WriteLine($"  Token: {tokensColor}{budget.TokensUsed}/{budget.TokensBudget}{AnsiStyleConstants.Reset} (剩余 {budget.TokensRemaining})");

        if (budget.IsAnyExhausted)
        {
            var cause = budget.ExhaustionCause switch
            {
                BudgetExhaustionCause.Rounds => "轮次先触底",
                BudgetExhaustionCause.Tokens => "Token先触底",
                BudgetExhaustionCause.Both => "同时触底",
                _ => string.Empty,
            };
            TerminalHelper.WriteLine($"  {TerminalColors.Warning}⚠ {cause} — 使用 /falv --continue 续费{AnsiStyleConstants.Reset}");
        }
    }

    private static void ShowVerdicts(IReasoningEngine engine)
    {
        TerminalHelper.WriteLine("=== 裁决结果 ===");
        foreach (var fact in engine.GetFacts())
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Primary}事实{AnsiStyleConstants.Reset}: {fact.Content} (置信度:{fact.Confidence}%)");
        }

        var rejected = engine.GetAllItems().Where(x => x.State == DataState.Rejected);
        foreach (var item in rejected)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Error}被驳斥{AnsiStyleConstants.Reset}: {item.Content}");
        }

        var pending = engine.GetAllItems().Where(x => x.State == DataState.PendingEvidence);
        foreach (var item in pending)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Warning}待补充{AnsiStyleConstants.Reset}: {item.Content}");
        }
    }

    private static void ShowEvidence(IReasoningEngine engine)
    {
        TerminalHelper.WriteLine("=== 证据链 ===");
        foreach (var ev in engine.GetAllEvidence())
        {
            TerminalHelper.WriteLine($"  [{ev.Category}] {ev.Content} (信任度:{ev.TrustLevel}, 提交方:{ev.SubmittedBy})");
        }

        if (engine.GetAllEvidence().Count == 0)
        {
            TerminalHelper.WriteLine("  暂无证据");
        }
    }

    private static void ShowHelp()
    {
        TerminalHelper.WriteLine("用法: /falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --continue [rounds|tokens|both|default] | /falv --budget | /falv --reset");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("结构化推理引擎 — 假定→验证→事实 三态跃迁");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("  <内容>                          添加一个假定");
        TerminalHelper.WriteLine("  --status                        查看当前推理状态（含预算）");
        TerminalHelper.WriteLine("  --judge                         触发三权裁决（控方→辩方→法官）");
        TerminalHelper.WriteLine("  --evidence                      查看证据链");
        TerminalHelper.WriteLine("  --continue [rounds|tokens|both|default]  续费并继续推理");
        TerminalHelper.WriteLine("  --budget                        查看预算状态");
        TerminalHelper.WriteLine("  --reset                         重置推理引擎");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("续费方式:");
        TerminalHelper.WriteLine("  rounds   仅续费轮次预算");
        TerminalHelper.WriteLine("  tokens   仅续费Token预算");
        TerminalHelper.WriteLine("  both     同时续费轮次和Token");
        TerminalHelper.WriteLine("  default  按配置默认方式续费（仅续费已耗尽项）");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("证明标准预设（代码配置 ReasoningPreset 枚举）:");
        TerminalHelper.WriteLine("  Murder   杀人罪 — 证据阈值最高，闭环锁死，排除合理怀疑");
        TerminalHelper.WriteLine("  Panda    吃熊猫罪 — 证据阈值动态，视情节浮动（默认）");
        TerminalHelper.WriteLine("  Divorce  离婚官司 — 证据阈值最低，高度盖然性即可");
    }
}
