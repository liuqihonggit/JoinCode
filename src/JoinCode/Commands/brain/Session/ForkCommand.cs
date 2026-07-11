namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Fork, Description = "创建当前对话的分支", Usage = "/fork [name]", Category = ChatCommandCategory.Session)]
public sealed class ForkCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Fork;
    public string Description => "创建当前对话的分支";
    public string Usage => "/fork [name]";
    public string[] Aliases => ["branch"];
    public string ArgumentHint => "[name]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var transcriptService = ChatCommandBase.GetService<JoinCode.Abstractions.Interfaces.ITranscriptService>(context, typeof(JoinCode.Abstractions.Interfaces.ITranscriptService));

        if (transcriptService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}会话转录服务不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var currentSessionId = context.SessionId;
        if (string.IsNullOrEmpty(currentSessionId))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}当前无活跃会话{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var customTitle = ChatCommandBase.GetNormalizedArgs(context);

        try
        {
            var entries = await transcriptService.LoadTranscriptAsync(
                currentSessionId, context.CancellationToken).ConfigureAwait(false);

            if (entries.Count == 0)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}当前会话没有消息，无法创建分支{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            var forkSessionId = Guid.NewGuid().ToString("N");

            var mainEntries = entries
                .Where(e => !e.IsSidechain)
                .ToList();

            if (mainEntries.Count == 0)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}没有可分支的主对话消息{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            var forkEntries = new List<JoinCode.Abstractions.LLM.Chat.TranscriptEntry>();
            foreach (var entry in mainEntries)
            {
                var forkedEntry = new JoinCode.Abstractions.LLM.Chat.TranscriptEntry
                {
                    SessionId = forkSessionId,
                    Role = entry.Role,
                    Content = entry.Content,
                    Timestamp = entry.Timestamp,
                    ModelId = entry.ModelId,
                    PromptTokens = entry.PromptTokens,
                    CompletionTokens = entry.CompletionTokens,
                    AgentId = entry.AgentId,
                    IsSidechain = false,
                    ToolName = entry.ToolName,
                    ToolUseId = entry.ToolUseId,
                };
                forkEntries.Add(forkedEntry);
            }

            await transcriptService.AppendEntriesAsync(
                forkSessionId, forkEntries, context.CancellationToken).ConfigureAwait(false);

            var firstPrompt = DeriveFirstPrompt(mainEntries);
            var baseName = !string.IsNullOrEmpty(customTitle) ? customTitle : firstPrompt;
            var effectiveTitle = $"{baseName} (Branch)";

            TerminalHelper.WriteLine($"{TerminalColors.Success}已创建对话分支{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  分支 ID: {forkSessionId}");
            TerminalHelper.WriteLine($"  标题: {effectiveTitle}");
            TerminalHelper.WriteLine($"  消息数: {forkEntries.Count}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"使用 /resume {forkSessionId} 恢复到分支");
            TerminalHelper.WriteLine($"使用 /resume {currentSessionId} 返回原始会话");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("创建分支", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static string DeriveFirstPrompt(IReadOnlyList<JoinCode.Abstractions.LLM.Chat.TranscriptEntry> entries)
    {
        var firstUser = entries.FirstOrDefault(e =>
            e.Role.Equals(MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase));

        if (firstUser is null)
            return "Branched conversation";

        var content = firstUser.Content.Trim();
        if (string.IsNullOrEmpty(content))
            return "Branched conversation";

        var singleLine = content.Replace('\n', ' ').Replace('\r', ' ');
        while (singleLine.Contains("  ", StringComparison.Ordinal))
            singleLine = singleLine.Replace("  ", " ", StringComparison.Ordinal);

        return singleLine.Length > 100
            ? string.Concat(singleLine.AsSpan(0, 97), "...")
            : singleLine;
    }
}
