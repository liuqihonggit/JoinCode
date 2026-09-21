
namespace Testing.Common.MockServer;

public sealed class MockChatCompletionResponse {
    /// <summary>获取响应标识</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>获取对象类型</summary>
    public string Object { get; init; } = string.Empty;
    /// <summary>获取创建时间戳</summary>
    public long Created { get; init; }
    /// <summary>获取模型名称</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>获取候选结果列表</summary>
    public List<MockChatChoice> Choices { get; init; } = new();
    /// <summary>获取用量统计</summary>
    public MockUsage? Usage { get; init; }
}

public sealed class MockChatChoice {
    /// <summary>获取候选索引</summary>
    public int Index { get; init; }
    /// <summary>获取消息内容</summary>
    public MockApiMessage? Message { get; init; }
    /// <summary>获取增量内容</summary>
    public MockChatDelta? Delta { get; init; }
    /// <summary>获取结束原因</summary>
    public string? FinishReason { get; init; }
}

public sealed class MockApiMessage {
    /// <summary>获取角色类型</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>获取文本内容</summary>
    public string? Content { get; init; }
    /// <summary>获取工具调用列表</summary>
    public List<MockToolCall> ToolCalls { get; init; } = [];
}

public sealed class MockToolCall {
    /// <summary>获取调用标识</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>获取调用类型</summary>
    public string Type { get; init; } = "function";
    /// <summary>获取函数调用信息</summary>
    public MockToolCallFunction Function { get; init; } = new();
}

public sealed class MockToolCallFunction {
    /// <summary>获取函数名称</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取函数参数</summary>
    public string Arguments { get; init; } = "{}";
}

public sealed class MockChatDelta {
    /// <summary>获取增量文本内容</summary>
    public string? Content { get; init; }
    /// <summary>获取增量工具调用列表</summary>
    public List<MockToolCallDelta> ToolCalls { get; init; } = [];
}

public sealed class MockToolCallDelta {
    /// <summary>获取候选索引</summary>
    public int Index { get; init; }
    /// <summary>获取调用标识</summary>
    public string? Id { get; init; }
    /// <summary>获取调用类型</summary>
    public string? Type { get; init; }
    /// <summary>获取函数调用增量信息</summary>
    public MockToolCallFunctionDelta? Function { get; init; }
}

public sealed class MockToolCallFunctionDelta {
    /// <summary>获取函数名称</summary>
    public string? Name { get; init; }
    /// <summary>获取函数参数</summary>
    public string? Arguments { get; init; }
}

public sealed class MockUsage {
    /// <summary>获取提示词令牌数</summary>
    public int PromptTokens { get; init; }
    /// <summary>获取补全令牌数</summary>
    public int CompletionTokens { get; init; }
    /// <summary>获取总令牌数</summary>
    public int TotalTokens { get; init; }
}

public sealed class MockModelsResponse {
    /// <summary>获取对象类型</summary>
    public string Object { get; init; } = "list";
    /// <summary>获取模型列表</summary>
    public List<MockModelItem> Data { get; init; } = new();
}

public sealed class MockModelItem {
    /// <summary>获取模型标识</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>获取对象类型</summary>
    public string Object { get; init; } = "model";
    /// <summary>获取创建时间戳</summary>
    public long Created { get; init; }
    /// <summary>获取归属方</summary>
    public string OwnedBy { get; init; } = string.Empty;
}