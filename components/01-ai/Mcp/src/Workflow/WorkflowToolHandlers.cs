


namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Workflow)]
public class WorkflowToolHandlers
{
    private readonly IPlanService? _planService;
    private readonly IChatService? _chatService;
    private readonly ICodeService? _codeService;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _historyLock = new(1, 1);

    // 内存中的对话历史（用于提示词模式下的多轮对话测试）
    private readonly List<ApiMessageRecord> _inMemoryMessageList = new();

    public WorkflowToolHandlers(
        IPlanService? planService,
        IChatService? chatService,
        ICodeService? codeService,
        IConfiguration configuration)
    {
        _planService = planService;
        _chatService = chatService;
        _codeService = codeService;
        _configuration = configuration;
    }

    private bool CheckHasAiKey()
    {
        var apiKey = _configuration["Workflow:Provider:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) return true;

        var envApiKey = Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiApiKey.ToValue());
        return !string.IsNullOrWhiteSpace(envApiKey);
    }

    private bool IsPromptOnlyMode()
    {
        var modeConfig = _configuration?["McpServer:OperationMode"]?.ToLowerInvariant();
        var hasAiKey = CheckHasAiKey();
        var hasRequiredServices = _planService != null && _chatService != null && _codeService != null;

        return modeConfig == "promptonly" ||
               (modeConfig != "aikey" && (!hasAiKey || !hasRequiredServices));
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowExecute, "Execute workflow tasks for running and starting various automated workflows", "execution")]
    public Task<ToolResult> WorkflowExecuteAsync(
        [McpToolParameter("Workflow task description, e.g.: Analyze code performance issues and provide optimization suggestions")] string task,
        CancellationToken cancellationToken = default)
    {
        var command = new ExecuteWorkflowCommand(task);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.WorkflowExecute(cmd.Task)),
            async (cmd, ct) =>
            {
                var result = await (_planService ?? throw new InvalidOperationException("PlanService is not available")).ExecutePlanAsync(cmd.Task, ct);
                return McpResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowPlanCreateAndExecute, "Create and execute plans for complex task planning", "execution")]
    public Task<ToolResult> PlanCreateAndExecuteAsync(
        [McpToolParameter("User task description, AI will create and execute a plan based on this, e.g.: Create a REST API project structure")] string prompt,
        CancellationToken cancellationToken = default)
    {
        var command = new CreatePlanCommand(prompt);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.PlanCreateAndExecute(cmd.Prompt)),
            async (cmd, ct) =>
            {
                var result = await (_planService ?? throw new InvalidOperationException("PlanService is not available")).ExecutePlanAsync(cmd.Prompt, ct);
                return McpResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowGenerateCode, "Generate code for writing programs, implementing features and developing modules", "code")]
    public Task<ToolResult> WorkflowGenerateCodeAsync(
        [McpToolParameter("Code requirement description, e.g.: Create a user authentication service with login and registration")] string requirement,
        CancellationToken cancellationToken = default)
    {
        var command = new GenerateCodeCommand(requirement);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.GenerateCode(cmd.Requirement)),
            async (cmd, ct) =>
            {
                var result = await (_codeService ?? throw new InvalidOperationException("CodeService is not available")).GenerateCodeAsync(cmd.Requirement, ct);
                return McpResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowAnalyzeCode, "Analyze code for code review, bug detection, optimization suggestions and security audit", "code")]
    public Task<ToolResult> WorkflowAnalyzeCodeAsync(
        [McpToolParameter("Code to analyze")] string code,
        [McpToolParameter("Analysis type: general, bugs, optimize, security", Required = false, DefaultValue = "general")] string analysisType = "general",
        CancellationToken cancellationToken = default)
    {
        var analysisTypeEnum = AnalysisTypeExtensions.FromValue(analysisType) ?? AnalysisType.General;
        var command = new AnalyzeCodeCommand(code, analysisTypeEnum.ToValue());
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) =>
            {
                var analysisPrompt = PromptTemplates.GetAnalysisPrompt(cmd.AnalysisType);
                return Task.FromResult(PromptTemplates.AnalyzeCode(cmd.AnalysisType, analysisPrompt, cmd.Code));
            },
            async (cmd, ct) =>
            {
                var result = await (_codeService ?? throw new InvalidOperationException("CodeService is not available")).AnalyzeCodeAsync(cmd.Code, ct);
                return McpResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowChat, "Chat with AI for communication and Q&A", "chat")]
    public Task<ToolResult> WorkflowChatAsync(
        [McpToolParameter("Message content")] string message,
        CancellationToken cancellationToken = default)
    {
        var command = new ChatCommand(message);
        return ExecuteWithValidationAsync(
            command,
            async (cmd, ct) =>
            {
                // 在提示词模式下，也记录对话历史
                await RecordApiMessageAsync(MessageRoleConstants.User, cmd.Message, ct);
                var prompt = PromptTemplates.Chat(cmd.Message);
                // 模拟助手回复
                await RecordApiMessageAsync(MessageRoleConstants.Assistant, L.T(StringKey.WorkflowPromptModeReceivedMessage, cmd.Message), ct);
                return prompt;
            },
            async (cmd, ct) =>
            {
                var result = await (_chatService ?? throw new InvalidOperationException("ChatService is not available")).SendMessageAsync(cmd.Message);
                return McpResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowClearHistory, "Clear chat history", "chat")]
    public async Task<ToolResult> WorkflowClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (IsPromptOnlyMode())
        {
            // 在提示词模式下，清空内存历史
            await ClearInMemoryHistoryAsync(cancellationToken);
            return McpResultBuilder.Success().WithText(L.T(StringKey.WorkflowPromptModeHistoryCleared)).Build();
        }

        if (_chatService == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorkflowChatServiceUnavailable)).Build();
        }

        await _chatService.ClearHistoryAsync();
        return McpResultBuilder.Success().WithText(L.T(StringKey.WorkflowChatHistoryCleared)).Build();
    }

    [McpTool(WorkflowToolNameConstants.McpAiWorkflowWorkflowGetHistory, "Get chat history records", "chat")]
    public async Task<ToolResult> WorkflowGetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (IsPromptOnlyMode())
        {
            // 在提示词模式下，返回内存中的历史
            var history = await GetInMemoryHistoryAsync(cancellationToken);
            if (history.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.WorkflowPromptModeNoHistory)).Build();
            }

            var formattedHistory = string.Join("\n\n", history.Select(m => $"[{m.Role}]: {m.Content}"));
            return McpResultBuilder.Success().WithText(formattedHistory).Build();
        }

        if (_chatService == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorkflowChatServiceUnavailable)).Build();
        }

        var serviceHistory = await _chatService.GetMessageListAsync();
        if (serviceHistory == null || serviceHistory.Count == 0)
        {
            return McpResultBuilder.Success().WithText(L.T(StringKey.WorkflowNoChatHistory)).Build();
        }

        var formattedServiceHistory = string.Join("\n\n", serviceHistory.Select(m => $"[{m.Role}]: {m.Content}"));
        return McpResultBuilder.Success().WithText(formattedServiceHistory).Build();
    }

    #region In-Memory Chat History (for Prompt-Only Mode Testing)

    private async Task RecordApiMessageAsync(string role, string content, CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct);
        try
        {
            _inMemoryMessageList.Add(new ApiMessageRecord(role, content, DateTime.UtcNow));
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private async Task ClearInMemoryHistoryAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct);
        try
        {
            _inMemoryMessageList.Clear();
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private async Task<IReadOnlyList<ApiMessageRecord>> GetInMemoryHistoryAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct);
        try
        {
            return new List<ApiMessageRecord>(_inMemoryMessageList);
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private record ApiMessageRecord(string Role, string Content, DateTime Timestamp);

    #endregion

    private async Task<ToolResult> ExecuteWithValidationAsync<TCommand>(
        TCommand command,
        Func<TCommand, CancellationToken, Task<string>> promptGenerator,
        Func<TCommand, CancellationToken, Task<ToolResult>> execution,
        CancellationToken cancellationToken)
    {
        var validationResult = ValidateCommand(command);
        if (validationResult != null)
        {
            return validationResult;
        }

        if (IsPromptOnlyMode())
        {
            var prompt = await promptGenerator(command, cancellationToken);
            return McpResultBuilder.Success().WithText(prompt).Build();
        }

        if (_planService == null || _chatService == null || _codeService == null)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.WorkflowAiServiceUnavailable))
                .Build();
        }

        return await execution(command, cancellationToken);
    }

    private static ToolResult? ValidateCommand<TCommand>(TCommand command)
    {
        var validationError = command switch
        {
            ExecuteWorkflowCommand cmd => string.IsNullOrWhiteSpace(cmd.Task) ? L.T(StringKey.WorkflowTaskCannotBeEmpty) : null,
            CreatePlanCommand cmd => string.IsNullOrWhiteSpace(cmd.Prompt) ? L.T(StringKey.WorkflowPromptCannotBeEmpty) : null,
            GenerateCodeCommand cmd => string.IsNullOrWhiteSpace(cmd.Requirement) ? L.T(StringKey.WorkflowRequirementCannotBeEmpty) : null,
            AnalyzeCodeCommand cmd => string.IsNullOrWhiteSpace(cmd.Code) ? L.T(StringKey.WorkflowCodeCannotBeEmpty) : null,
            ChatCommand cmd => string.IsNullOrWhiteSpace(cmd.Message) ? L.T(StringKey.WorkflowMessageCannotBeEmpty) : null,
            _ => null
        };

        return validationError != null
            ? McpResultBuilder.Error().WithText(validationError).Build()
            : null;
    }
}
