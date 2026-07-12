namespace JoinCode.ChatCommands;

/// <summary>
/// /falv 命令 — 结构化推理（三权分立）
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Falv, Description = "结构化推理引擎（假定→验证→事实）", Usage = "/falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --reset", Category = ChatCommandCategory.Law)]
public sealed class FalvCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Falv;
    public string Description => "结构化推理引擎（假定→验证→事实）";
    public string Usage => "/falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --reset";
    public string[] Aliases => [];
    public string ArgumentHint => "<假定内容|--status|--judge|--evidence|--reset>";
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

        switch (args)
        {
            case "--status":
                ShowStatus(engine);
                break;
            case "--judge":
                await engine.RunAdversarialProcessAsync(context.CancellationToken).ConfigureAwait(false);
                ShowVerdicts(engine);
                break;
            case "--evidence":
                ShowEvidence(engine);
                break;
            case "--reset":
                TerminalHelper.WriteLine("推理引擎已重置（请重新创建引擎实例）");
                break;
            default:
                await AddAssumptionAsync(engine, args, context.CancellationToken).ConfigureAwait(false);
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
        TerminalHelper.WriteLine("用法: /falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --reset");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("结构化推理引擎 — 假定→验证→事实 三态跃迁");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("  <内容>      添加一个假定");
        TerminalHelper.WriteLine("  --status    查看当前推理状态");
        TerminalHelper.WriteLine("  --judge     触发三权裁决（控方→辩方→法官）");
        TerminalHelper.WriteLine("  --evidence  查看证据链");
        TerminalHelper.WriteLine("  --reset     重置推理引擎");
    }
}
