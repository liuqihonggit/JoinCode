
namespace Api.LLM.QueryServices.Jev;

/// <summary>
/// Jev QueryService — TypeSafe AI System One Model
/// 横跨两体系:实现 IQueryService(走 QueryServiceFactory 统一分派)+ ITypedDecisionService(强类型决策消费)
/// 协议:POST https://api.typesafe.ai/v1/systemone,请求 {model, state, questions},响应 {answers, usage}
/// </summary>
public class JevQueryService : QueryServiceBase, ITypedDecisionService {
    /// <summary>
    /// 构造 Jev QueryService
    /// </summary>
    public JevQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null,
        IFileSystem? fs = null, ResilientHttpExecutor? resilientExecutor = null)
        : base(config, httpClient, logger, fs, resilientExecutor) {
    }

    /// <summary>
    /// 非流式:把 MessageList 拼接为 state,用默认 noul question,调用 GetTypedDecisionsAsync,返回摘要 ApiMessage
    /// 真正的 Jev 功能通过 GetTypedDecisionsAsync 直接消费 ITypedDecisionService 使用
    /// </summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default) {
        var state = BuildStateFromMessageList(chatHistory);
        var defaultQuestion = new TypedDecisionQuestion {
            Kind = TypedDecisionKind.Noul,
            Instructions = "Analyze the input and provide a judgment."
        };
        var questions = new Dictionary<string, TypedDecisionQuestion> {
            ["default"] = defaultQuestion
        };

        var result = await GetTypedDecisionsAsync(state, questions, cancellationToken).ConfigureAwait(false);
        return [ConvertToApiMessage(result)];
    }

    /// <summary>
    /// 流式:Jev 不支持流式(端点 systemone 是非流式决策端点),降级为非流式包装单次 yield
    /// </summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var messages = await GetApiMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false);
        foreach (var msg in messages) {
            yield return new StreamEvent(msg.Role, msg.Content, msg.ModelId, msg.Metadata);
        }
    }

    /// <summary>
    /// 类型化决策查询 — Jev 核心入口
    /// 构建 JevRequest → POST systemone → 反序列化 JevResponse → TypedDecisionResult
    /// </summary>
    public async Task<TypedDecisionResult> GetTypedDecisionsAsync(
        string state,
        IReadOnlyDictionary<string, TypedDecisionQuestion> questions,
        CancellationToken cancellationToken = default) {
        var request = BuildJevRequest(state, questions);
        var json = JsonSerializer.Serialize(request, JevJsonContext.Default.JevRequest);
        var endpoint = GetChatEndpoint(Config);

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.Jev", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var jevResponse = JsonSerializer.Deserialize(responseJson, JevJsonContext.Default.JevResponse)
            ?? throw new InvalidOperationException("Jev 响应反序列化失败:返回 null");

        return ConvertToTypedDecisionResult(jevResponse);
    }

    /// <summary>
    /// 从 MessageList 构建 state — 拼接所有消息为单个字符串(决策3:方案 a)
    /// </summary>
    private static string BuildStateFromMessageList(MessageList chatHistory) {
        var sb = new StringBuilder();
        foreach (var msg in chatHistory) {
            var role = msg.Role switch {
                MessageRole.System => "[System]",
                MessageRole.User => "[User]",
                MessageRole.Assistant => "[Assistant]",
                MessageRole.Tool => "[Tool]",
                _ => "[Unknown]"
            };
            sb.Append(role).Append(": ").AppendLine(msg.Content);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 构建 JevRequest — state 转为 JsonElement,questions 映射为 JevQuestion
    /// </summary>
    private JevRequest BuildJevRequest(string state, IReadOnlyDictionary<string, TypedDecisionQuestion> questions) {
        var jevQuestions = new Dictionary<string, JevQuestion>();
        foreach (var kvp in questions) {
            jevQuestions[kvp.Key] = new JevQuestion {
                Type = kvp.Value.Kind.ToValue(),
                Instructions = kvp.Value.Instructions,
                Options = kvp.Value.Options.Count > 0 ? kvp.Value.Options.ToList() : []
            };
        }
        return new JevRequest {
            Model = Config.ModelId,
            State = JsonElementHelper.FromString(state),
            Questions = jevQuestions
        };
    }

    /// <summary>
    /// JevResponse → TypedDecisionResult — answers 映射为 JevDecision 字典
    /// </summary>
    private static TypedDecisionResult ConvertToTypedDecisionResult(JevResponse response) {
        var decisions = new Dictionary<string, ITypedDecision>();
        foreach (var kvp in response.Answers) {
            decisions[kvp.Key] = ConvertToDecision(kvp.Key, kvp.Value);
        }
        return new TypedDecisionResult {
            Answers = decisions,
            ModelId = response.Model,
            Usage = response.Usage is not null ? new TypedDecisionUsage { InputTokens = response.Usage.InputTokens } : null
        };
    }

    /// <summary>
    /// JevAnswer → JevDecision — 根据 Noul/Choice/Score 哪个有值决定 Kind
    /// </summary>
    private static JevDecision ConvertToDecision(string questionName, JevAnswer answer) {
        if (answer.Noul.HasValue) {
            return new JevDecision {
                QuestionName = questionName,
                Kind = TypedDecisionKind.Noul,
                Confidence = answer.Confidence ?? 0,
                RawValue = JsonElementHelper.FromDouble(answer.Noul.Value)
            };
        }
        if (answer.Choice is not null) {
            return new JevDecision {
                QuestionName = questionName,
                Kind = TypedDecisionKind.Choice,
                Confidence = answer.Confidence ?? 0,
                RawValue = JsonElementHelper.FromString(answer.Choice)
            };
        }
        if (answer.Score.HasValue) {
            return new JevDecision {
                QuestionName = questionName,
                Kind = TypedDecisionKind.Score,
                Confidence = answer.Confidence ?? 0,
                RawValue = JsonElementHelper.FromDouble(answer.Score.Value)
            };
        }
        return new JevDecision {
            QuestionName = questionName,
            Kind = TypedDecisionKind.Noul,
            Confidence = 0,
            RawValue = JsonElementHelper.FromDouble(0.0)
        };
    }

    /// <summary>
    /// TypedDecisionResult → ApiMessage — 决策摘要塞 Content,完整决策通过 ITypedDecisionService 消费
    /// </summary>
    private static ApiMessage ConvertToApiMessage(TypedDecisionResult result) {
        var sb = new StringBuilder();
        foreach (var kvp in result.Answers) {
            var decision = kvp.Value;
            var valueStr = decision.Kind switch {
                TypedDecisionKind.Noul => $"noul={decision.RawValue.GetDouble()}",
                TypedDecisionKind.Choice => $"choice={decision.RawValue.GetString()}",
                TypedDecisionKind.Score => $"score={decision.RawValue.GetDouble()}",
                _ => "unknown"
            };
            sb.Append(decision.QuestionName).Append(": ").Append(valueStr)
              .Append(" (confidence=").Append(decision.Confidence.ToString("F2")).AppendLine(")");
        }
        var metadata = new Dictionary<string, JsonElement> {
            ["JevModelId"] = result.ModelId is not null ? JsonElementHelper.FromString(result.ModelId) : JsonElementHelper.NullElement(),
            ["JevInputTokens"] = result.Usage is not null ? JsonElementHelper.FromInt32(result.Usage.InputTokens) : JsonElementHelper.NullElement()
        };
        return new ApiMessage(MessageRole.Assistant, sb.ToString().TrimEnd(), metadata, result.ModelId);
    }
}
