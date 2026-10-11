


namespace McpToolDispatch;

/// <summary>
/// 工作流工具处理器 — 提供工作流执行、计划创建、代码生成、代码分析、聊天、历史管理功能
/// </summary>
[McpToolDispatch(ToolCategory.Workflow)]
public class WorkflowToolHandlers {
    private readonly IPlanService? _planService;
    private readonly IChatService? _chatService;
    private readonly ICodeService? _codeService;
    private readonly IConfiguration _configuration;
    private readonly IFileSystem? _fileSystem;
    private readonly ILogger<WorkflowToolHandlers>? _logger;
    private readonly AsyncLock _historyLock = new();

    // 内存中的对话历史（用于提示词模式下的多轮对话测试）
    private readonly List<ApiMessageRecord> _inMemoryMessageList = new();

    /// <summary>
    /// 初始化工作流工具处理器
    /// </summary>
    /// <param name="planService">计划服务（可选）</param>
    /// <param name="chatService">聊天服务（可选）</param>
    /// <param name="codeService">代码服务（可选）</param>
    /// <param name="configuration">配置接口</param>
    /// <param name="fileSystem">文件系统抽象（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public WorkflowToolHandlers(
        IPlanService? planService,
        IChatService? chatService,
        ICodeService? codeService,
        IConfiguration configuration,
        IFileSystem? fileSystem = null,
        ILogger<WorkflowToolHandlers>? logger = null) {
        _planService = planService;
        _chatService = chatService;
        _codeService = codeService;
        _configuration = configuration;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    private async Task<bool> CheckHasAiKeyAsync() {
        var apiKey = _configuration["Workflow:Provider:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) return true;

        foreach (var envVar in Enum.GetValues<ProviderEnvVar>()) {
            var envValue = Environment.GetEnvironmentVariable(envVar.ToValue());
            if (!string.IsNullOrWhiteSpace(envValue)) return true;
        }

        var authFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.AuthFileName);
        try {
            var fs = _fileSystem ?? new IO.FileSystem.PhysicalFileSystem();
            if (fs.FileExists(authFilePath)) {
                var json = await fs.ReadAllText(authFilePath).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject()) {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.Value.GetString())) {
                        return true;
                    }
                }
            }
        } catch (Exception ex) {
            _logger?.LogError("读取 auth.json 失败: {Message}", ex.Message);
        }

        return false;
    }

    private async Task<bool> IsPromptOnlyModeAsync() {
        var modeConfig = _configuration?["McpServer:OperationMode"]?.ToLowerInvariant();
        var hasAiKey = await CheckHasAiKeyAsync().ConfigureAwait(false);
        var hasRequiredServices = _planService != null && _chatService != null && _codeService != null;

        return modeConfig == "promptonly" ||
               (modeConfig != "aikey" && (!hasAiKey || !hasRequiredServices));
    }

    /// <summary>
    /// 执行工作流任务 — 运行和启动各种自动化工作流
    /// </summary>
    /// <param name="task">工作流任务描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowExecute, "Execute workflow tasks for running and starting various automated workflows", "execution")]
    public Task<ToolResult> WorkflowExecuteAsync(
        [McpToolParameter("Workflow task description, e.g.: Analyze code performance issues and provide optimization suggestions")] string task,
        CancellationToken cancellationToken = default) {
        var command = new ExecuteWorkflowCommand(task);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.WorkflowExecute(cmd.Task)),
            async (cmd, ct) => {
                var result = await (_planService ?? throw new InvalidOperationException("PlanService is not available")).ExecutePlanAsync(cmd.Task, ct).ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    /// <summary>
    /// 创建并执行计划 — 针对复杂任务进行规划并执行
    /// </summary>
    /// <param name="prompt">用户任务描述，AI 将据此创建并执行计划</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowPlanCreateAndExecute, "Create and execute plans for complex task planning", "execution")]
    public Task<ToolResult> PlanCreateAndExecuteAsync(
        [McpToolParameter("User task description, AI will create and execute a plan based on this, e.g.: Create a REST API project structure")] string prompt,
        CancellationToken cancellationToken = default) {
        var command = new CreatePlanCommand(prompt);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.PlanCreateAndExecute(cmd.Prompt)),
            async (cmd, ct) => {
                var result = await (_planService ?? throw new InvalidOperationException("PlanService is not available")).ExecutePlanAsync(cmd.Prompt, ct).ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    /// <summary>
    /// 生成代码 — 编写程序、实现功能、开发模块
    /// </summary>
    /// <param name="requirement">代码需求描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowGenerateCode, "Generate code for writing programs, implementing features and developing modules", "code")]
    public Task<ToolResult> WorkflowGenerateCodeAsync(
        [McpToolParameter("Code requirement description, e.g.: Create a user authentication service with login and registration")] string requirement,
        CancellationToken cancellationToken = default) {
        var command = new GenerateCodeCommand(requirement);
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => Task.FromResult(PromptTemplates.GenerateCode(cmd.Requirement)),
            async (cmd, ct) => {
                var result = await (_codeService ?? throw new InvalidOperationException("CodeService is not available")).GenerateCodeAsync(cmd.Requirement, ct).ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    /// <summary>
    /// 分析代码 — 代码审查、Bug 检测、优化建议、安全审计
    /// </summary>
    /// <param name="code">待分析的代码</param>
    /// <param name="analysisType">分析类型：general/bugs/optimize/security（默认 general）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowAnalyzeCode, "Analyze code for code review, bug detection, optimization suggestions and security audit", "code")]
    public Task<ToolResult> WorkflowAnalyzeCodeAsync(
        [McpToolParameter("Code to analyze")] string code,
        [McpToolParameter("Analysis type: general, bugs, optimize, security", Required = false, DefaultValue = "general")] string analysisType = "general",
        CancellationToken cancellationToken = default) {
        var analysisTypeEnum = AnalysisTypeExtensions.FromValue(analysisType) ?? AnalysisType.General;
        var command = new AnalyzeCodeCommand(code, analysisTypeEnum.ToValue());
        return ExecuteWithValidationAsync(
            command,
            (cmd, ct) => {
                var analysisPrompt = PromptTemplates.GetAnalysisPrompt(cmd.AnalysisType);
                return Task.FromResult(PromptTemplates.AnalyzeCode(cmd.AnalysisType, analysisPrompt, cmd.Code));
            },
            async (cmd, ct) => {
                var result = await (_codeService ?? throw new InvalidOperationException("CodeService is not available")).AnalyzeCodeAsync(cmd.Code, ct).ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    /// <summary>
    /// 与 AI 聊天 — 通信和问答
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowChat, "Chat with AI for communication and Q&A", "chat")]
    public Task<ToolResult> WorkflowChatAsync(
        [McpToolParameter("Message content")] string message,
        CancellationToken cancellationToken = default) {
        var command = new ChatCommand(message);
        return ExecuteWithValidationAsync(
            command,
            async (cmd, ct) => {
                // 在提示词模式下，也记录对话历史
                await RecordApiMessageAsync(MessageRoleEnumConstants.User, cmd.Message, ct).ConfigureAwait(false);
                var prompt = PromptTemplates.Chat(cmd.Message);
                // 模拟助手回复
                await RecordApiMessageAsync(MessageRoleEnumConstants.Assistant, L.T(StringKey.WorkflowPromptModeReceivedMessage, cmd.Message), ct).ConfigureAwait(false);
                return prompt;
            },
            async (cmd, ct) => {
                var result = await (_chatService ?? throw new InvalidOperationException("ChatService is not available")).SendMessageAsync(cmd.Message).ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText(result).Build();
            },
            cancellationToken);
    }

    /// <summary>
    /// 清空聊天历史记录
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowClearHistory, "Clear chat history", "chat")]
    public async Task<ToolResult> WorkflowClearHistoryAsync(CancellationToken cancellationToken = default) {
        if (await IsPromptOnlyModeAsync().ConfigureAwait(false)) {
            // 在提示词模式下，清空内存历史
            await ClearInMemoryHistoryAsync(cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(L.T(StringKey.WorkflowPromptModeHistoryCleared)).Build();
        }

        if (_chatService == null) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.WorkflowChatServiceUnavailable)).Build();
        }

        await _chatService.ClearHistoryAsync().ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText(L.T(StringKey.WorkflowChatHistoryCleared)).Build();
    }

    /// <summary>
    /// 获取聊天历史记录
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含聊天历史的工具执行结果</returns>
    [McpTool(WorkflowToolNameEnumConstants.McpAiWorkflowWorkflowGetHistory, "Get chat history records", "chat")]
    public async Task<ToolResult> WorkflowGetHistoryAsync(CancellationToken cancellationToken = default) {
        if (await IsPromptOnlyModeAsync().ConfigureAwait(false)) {
            // 在提示词模式下，返回内存中的历史
            var history = await GetInMemoryHistoryAsync(cancellationToken).ConfigureAwait(false);
            if (history.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.WorkflowPromptModeNoHistory)).Build();
            }

            var formattedHistory = string.Join("\n\n", history.Select(m => $"[{m.Role}]: {m.Content}"));
            return ToolResultBuilder.Success().WithText(formattedHistory).Build();
        }

        if (_chatService == null) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.WorkflowChatServiceUnavailable)).Build();
        }

        var serviceHistory = await _chatService.GetMessageListAsync().ConfigureAwait(false);
        if (serviceHistory == null || serviceHistory.Count == 0) {
            return ToolResultBuilder.Success().WithText(L.T(StringKey.WorkflowNoChatHistory)).Build();
        }

        var formattedServiceHistory = string.Join("\n\n", serviceHistory.Select(m => $"[{m.Role}]: {m.Content}"));
        return ToolResultBuilder.Success().WithText(formattedServiceHistory).Build();
    }

    #region In-Memory Chat History (for Prompt-Only Mode Testing)

    private async Task RecordApiMessageAsync(string role, string content, CancellationToken ct = default) {
        using var guard = await _historyLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_historyLock.Name}' 等待超时");
        _inMemoryMessageList.Add(new ApiMessageRecord(role, content, DateTime.UtcNow));
    }

    private async Task ClearInMemoryHistoryAsync(CancellationToken ct = default) {
        using var guard = await _historyLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_historyLock.Name}' 等待超时");
        _inMemoryMessageList.Clear();
    }

    private async Task<IReadOnlyList<ApiMessageRecord>> GetInMemoryHistoryAsync(CancellationToken ct = default) {
        using var guard = await _historyLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_historyLock.Name}' 等待超时");
        return new List<ApiMessageRecord>(_inMemoryMessageList);
    }

    private record ApiMessageRecord(string Role, string Content, DateTime Timestamp);

    #endregion

    private async Task<ToolResult> ExecuteWithValidationAsync<TCommand>(
        TCommand command,
        Func<TCommand, CancellationToken, Task<string>> promptGenerator,
        Func<TCommand, CancellationToken, Task<ToolResult>> execution,
        CancellationToken cancellationToken) {
        var validationResult = ValidateCommand(command);
        if (validationResult != null) {
            return validationResult;
        }

        if (await IsPromptOnlyModeAsync().ConfigureAwait(false)) {
            var prompt = await promptGenerator(command, cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(prompt).Build();
        }

        if (_planService == null || _chatService == null || _codeService == null) {
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.WorkflowAiServiceUnavailable))
                .Build();
        }

        return await execution(command, cancellationToken).ConfigureAwait(false);
    }

    private static ToolResult? ValidateCommand<TCommand>(TCommand command) {
        var validationError = command switch {
            ExecuteWorkflowCommand cmd => string.IsNullOrWhiteSpace(cmd.Task) ? L.T(StringKey.WorkflowTaskCannotBeEmpty) : null,
            CreatePlanCommand cmd => string.IsNullOrWhiteSpace(cmd.Prompt) ? L.T(StringKey.WorkflowPromptCannotBeEmpty) : null,
            GenerateCodeCommand cmd => string.IsNullOrWhiteSpace(cmd.Requirement) ? L.T(StringKey.WorkflowRequirementCannotBeEmpty) : null,
            AnalyzeCodeCommand cmd => string.IsNullOrWhiteSpace(cmd.Code) ? L.T(StringKey.WorkflowCodeCannotBeEmpty) : null,
            ChatCommand cmd => string.IsNullOrWhiteSpace(cmd.Message) ? L.T(StringKey.WorkflowMessageCannotBeEmpty) : null,
            _ => null
        };

        return validationError != null
            ? ToolResultBuilder.Error().WithText(validationError).Build()
            : null;
    }
}