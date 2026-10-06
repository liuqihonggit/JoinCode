namespace McpToolDispatch;

/// <summary>
/// GitHub API 请求/响应 DTO — 固定结构 JSON 用 DTO + JsonContext 双向转换（编译期类型安全）
/// <para>替代手动拼接 JSON 字符串和 JsonDocument 动态解析，符合 NativeAOT + JsonContext 约束</para>
/// </summary>

/// <summary>Gist create 请求 body — POST /gists</summary>
internal sealed class GistCreateRequest {
    /// <summary>文件名 → 内容</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileContent> Files { get; init; } = new();

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }
}

/// <summary>Gist 文件内容</summary>
internal sealed class GistFileContent {
    /// <summary>文件内容</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

/// <summary>Gist 响应 — GET /gists/{id}</summary>
internal sealed class GistResponse {
    /// <summary>Gist ID</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }

    /// <summary>文件映射</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileDetail> Files { get; init; } = new();

    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

/// <summary>Gist 文件详情</summary>
internal sealed class GistFileDetail {
    /// <summary>文件名</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    /// <summary>文件内容</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>Gist list 响应元素 — GET /gists</summary>
internal sealed class GistListItem {
    /// <summary>Gist ID</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }

    /// <summary>文件映射</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileDetail> Files { get; init; } = new();
}

/// <summary>Workflow dispatch 请求 body — POST /actions/workflows/{id}/dispatches</summary>
internal sealed class WorkflowDispatchRequest {
    /// <summary>运行分支或 tag</summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    /// <summary>输入参数</summary>
    [JsonPropertyName("inputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string> Inputs { get; init; } = new();
}

/// <summary>Label create 请求 body — POST /labels</summary>
internal sealed class LabelCreateRequest {
    /// <summary>标签名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>颜色(6 字符 hex)</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>Deploy key add 请求 body — POST /keys</summary>
internal sealed class DeployKeyAddRequest {
    /// <summary>Key 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>SSH public key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    /// <summary>是否只读</summary>
    [JsonPropertyName("read_only")]
    public bool? ReadOnly { get; init; }
}

/// <summary>Autolink create 请求 body — POST /keys/autolinks</summary>
internal sealed class AutolinkCreateRequest {
    /// <summary>键前缀</summary>
    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; init; } = "";

    /// <summary>URL 模板</summary>
    [JsonPropertyName("url_template")]
    public string UrlTemplate { get; init; } = "";
}

/// <summary>SSH key add 请求 body — POST /user/keys</summary>
internal sealed class SshKeyAddRequest {
    /// <summary>Key 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>SSH public key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
}

/// <summary>GPG key add 请求 body — POST /user/gpg_keys</summary>
internal sealed class GpgKeyAddRequest {
    /// <summary>ASCII armored GPG key</summary>
    [JsonPropertyName("armored_public_key")]
    public string ArmoredPublicKey { get; init; } = "";
}

/// <summary>Variable set 请求 body — POST/PUT /actions/variables</summary>
internal sealed class VariableSetRequest {
    /// <summary>变量名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>变量值</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

/// <summary>
/// GitHub API DTO 的 JSON 序列化上下文 — AOT 模式需要源码生成器注册类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = false)]
[JsonSerializable(typeof(GistCreateRequest))]
[JsonSerializable(typeof(GistResponse))]
[JsonSerializable(typeof(GistListItem))]
[JsonSerializable(typeof(List<GistListItem>))]
[JsonSerializable(typeof(WorkflowDispatchRequest))]
[JsonSerializable(typeof(LabelCreateRequest))]
[JsonSerializable(typeof(DeployKeyAddRequest))]
[JsonSerializable(typeof(AutolinkCreateRequest))]
[JsonSerializable(typeof(SshKeyAddRequest))]
[JsonSerializable(typeof(GpgKeyAddRequest))]
[JsonSerializable(typeof(VariableSetRequest))]
internal sealed partial class GitHubApiJsonContext : JsonSerializerContext;
