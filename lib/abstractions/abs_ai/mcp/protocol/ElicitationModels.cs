namespace JoinCode.Abstractions.Mcp.Protocol;

public class ElicitRequestParams {
    /// <summary>获取或设置展示给用户的消息。</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>获取或设置 elicitation 模式（form/url）。</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ElicitMode.Form.ToValue();

    /// <summary>获取或设置请求的表单 schema。</summary>
    [JsonPropertyName("requestedSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElicitSchema? RequestedSchema { get; set; }

    /// <summary>获取或设置 URL 模式下的地址。</summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary>获取或设置 elicitation 标识。</summary>
    [JsonPropertyName("elicitationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElicitationId { get; set; }
}

public class ElicitSchema {
    /// <summary>获取或设置属性字典。</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, ElicitSchemaProperty> Properties { get; set; } = new();

    /// <summary>获取或设置必填字段名列表。</summary>
    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Required { get; set; } = [];
}

public class ElicitSchemaProperty : SchemaProperty {
    /// <summary>获取或设置属性标题。</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>获取或设置格式（如 date-time、email）。</summary>
    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }

    /// <summary>获取或设置字符串最小长度。</summary>
    [JsonPropertyName("minLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? MinLength { get; set; }

    /// <summary>获取或设置字符串最大长度。</summary>
    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? MaxLength { get; set; }

    /// <summary>获取或设置数值最小值。</summary>
    [JsonPropertyName("minimum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double? Minimum { get; set; }

    /// <summary>获取或设置数值最大值。</summary>
    [JsonPropertyName("maximum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double? Maximum { get; set; }

    /// <summary>获取或设置枚举可选值列表。</summary>
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Enum { get; set; } = [];

    /// <summary>获取或设置默认值。</summary>
    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Default { get; set; }

    /// <summary>获取或设置数组项的 schema。</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElicitSchemaProperty? Items { get; set; }
}

public class ElicitResult {
    /// <summary>获取或设置用户动作（accept/decline/cancel）。</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = ElicitAction.Cancel.ToValue();

    /// <summary>获取或设置表单内容字典。</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement?> Content { get; set; } = [];
}

public class ElicitationCompleteNotificationParams {
    /// <summary>获取或设置 elicitation 标识。</summary>
    [JsonPropertyName("elicitationId")]
    public string ElicitationId { get; set; } = string.Empty;
}

public enum ElicitAction {
    [EnumValue("accept")] Accept,
    [EnumValue("decline")] Decline,
    [EnumValue("cancel")] Cancel,
}

public enum ElicitMode {
    [EnumValue("form")] Form,
    [EnumValue("url")] Url,
}