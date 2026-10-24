namespace JoinCode.Gui.WebHost;

/// <summary>
/// 模型页供应商设置面（TASK066 C8）：把前端 <c>llm-deepseek</c> 命名空间投影到自有引擎的
/// <c>settings.json</c> / <c>providers_llm/{vendor}/config.json</c> / <c>models.json</c>。
/// <para>
/// 前端模型页先读 <c>llm/listConfigurableProviders</c> 得到「供应商 → 设置命名空间路径」，
/// 再经 <c>settings/describe</c> 取 <c>profiles.{vendor}</c> 的值/用户层/schema 渲染编辑卡片，
/// 保存时把改动作为 <c>settings/mutate</c> 的路径操作写回。宿主在这里完成三件事：
/// 声明可编辑供应商、投影引擎配置为 DSH 字段（baseURL/apiKeyEnv/model/models）、
/// 校验并落盘子文件后重载模型目录（下一次 <c>session/modelCatalog</c> 立即反映）。
/// </para>
/// <para>
/// 编辑边界：只开放连接字段；协议与供应商身份由引擎配置文件维护（模型页不提供删除/整段替换，
/// 因为供应商生命周期属于引擎）。引擎模型项没有 maxTokens 字段，携带该字段的写入显式拒绝并给出清空指引，
/// 不做静默丢弃。
/// </para>
/// </summary>
internal sealed class DshProviderSettingsStore {
    /// <summary>承载供应商配置的 settings 命名空间（前端 <c>layoutOf</c> 的 deepseek 布局）</summary>
    internal const string Namespace = "llm-deepseek";

    /// <summary>模型页允许编辑的 profile 字段（其余字段保持引擎现状）</summary>
    private static readonly string[] EditableFields = ["baseURL", "apiKeyEnv", "model", "models"];

    /// <summary>引擎模态标志中由模型页两态（text/image）拥有的位</summary>
    private const ModelModalityKind InputOwnedFlags = ModelModalityKind.Text | ModelModalityKind.ReadImage;

    private readonly IFileSystem _fs;
    private readonly string _baseDir;
    private readonly DshModelCatalogSource _models;

    /// <summary>串行化 describe/mutate（文件读写 + 目录重载不交错）</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 命名空间版本（前端 settings/mutate 的 expectedRevision 乐观并发）。
    /// 引擎供应商是虚拟命名空间（值由 providers_llm/ 文件投影，无独立文档），自身写入不推进版本：
    /// 前端模型页保存后不会把 mutate 应答折回 describe 镜像，推进版本会让它下一次保存直接冲突。
    /// </summary>
    private const int NamespaceRevision = 0;

    /// <summary>构造供应商设置面</summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="paths">应用数据路径（取 jcc 目录作为引擎配置根）</param>
    /// <param name="models">模型目录来源（写入后据此重载）</param>
    internal DshProviderSettingsStore(IFileSystem fs, AppDataPaths paths, DshModelCatalogSource models) {
        _fs = fs;
        _baseDir = paths.JccDirectory;
        _models = models;
    }

    /// <summary>settings.json 路径（模型目录重载入口）</summary>
    private string SettingsPath => Path.Combine(_baseDir, "settings.json");

    /// <summary>
    /// <c>llm/listConfigurableProviders</c>：把当前可路由供应商声明成模型页可编辑行。
    /// 路径统一为 <c>profiles/{vendor}</c>，命名空间统一 <see cref="Namespace"/>。
    /// </summary>
    internal List<DshConfigurableProvider> DeclareProviders() => _models.Current.Groups
        .Select(group => new DshConfigurableProvider {
            Provider = group.Id,
            DisplayName = string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name,
            SettingsNs = Namespace,
            SettingsPath = ["profiles", group.Id],
        })
        .ToList();

    /// <summary>
    /// <c>settings/describe</c> 中的供应商命名空间视图：值/用户层/基座层都是引擎配置投影
    /// （基座层同样含自定义供应商，模型页因此判定为不可删除 — 供应商生命周期归引擎）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshSettingsNamespaceView> DescribeAsync(CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return await BuildViewAsync(cancellationToken).ConfigureAwait(false);
        } finally {
            _gate.Release();
        }
    }

    /// <summary>
    /// <c>settings/mutate</c> 的供应商分支：版本校验 → 逐条路径操作 → 写回引擎配置文件
    /// → 重载模型目录 → 返回新视图（前端据此重渲染）。
    /// </summary>
    /// <param name="args">settings/mutate 参数（ns 必须是 <see cref="Namespace"/>）</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshRpcResult> MutateAsync(JsonElement args, CancellationToken cancellationToken) {
        if (ParseMutateArgs(args) is not { } request || !string.Equals(request.Ns, Namespace, StringComparison.Ordinal)) {
            return DshRpcResults.Fail("settings/rejected",
                $"settings/mutate 的 llm-deepseek 分支需要 {{ns:\"{Namespace}\", ops:[...]}}；收到 args={Describe(args)}。"
                + "编辑供应商连接请传路径 profiles/{供应商}/baseURL|apiKeyEnv|model|models。");
        }
        if (request.Ops.Count == 0) {
            return DshRpcResults.Fail("settings/rejected",
                $"settings/mutate 未提供 ops（ns={Namespace}）；至少给一条 set/unset 路径操作，例如 "
                + "{\"op\":\"set\",\"path\":[\"profiles\",\"deepseek\",\"baseURL\"],\"value\":\"https://api.deepseek.com/v1\"}。");
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (request.ExpectedRevision is { } expected && expected != NamespaceRevision) {
                return DshRpcResults.Fail("settings/conflict",
                    $"settings/mutate 期望版本 {expected}，实际版本 {NamespaceRevision}（ns={Namespace}）；设置页已变化，"
                    + "请重新读取请设置描述（settings/describe）后再提交。");
            }
            var profiles = await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in request.Ops) {
                if (Apply(profiles, op, SettingsPath) is { } error) {
                    return DshRpcResults.Fail("settings/rejected", error);
                }
                touched.Add(op.Path[1]);
            }
            foreach (var vendor in touched) {
                await PersistAsync(vendor, profiles[vendor], cancellationToken).ConfigureAwait(false);
            }
            await _models.LoadAsync(_fs, SettingsPath, cancellationToken).ConfigureAwait(false);
            return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
                await BuildViewAsync(cancellationToken).ConfigureAwait(false),
                DshJsonContext.Default.DshSettingsNamespaceView));
        } finally {
            _gate.Release();
        }
    }

    /// <summary>解析 mutate 参数（格式错误按拒绝返回 null）</summary>
    private static DshSettingsMutateArgs? ParseMutateArgs(JsonElement args) {
        try {
            return args.Deserialize(DshJsonContext.Default.DshSettingsMutateArgs);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>读取引擎合并配置里的供应商表（settings.json + providers_llm/{vendor}/ 子文件）</summary>
    private async Task<Dictionary<string, ProfileEdit>> LoadProfilesAsync(CancellationToken cancellationToken) {
        var settings = await SettingsLoader.LoadUserSettingsFromDirAsync(_fs, _baseDir, cancellationToken).ConfigureAwait(false);
        var profiles = new Dictionary<string, ProfileEdit>(StringComparer.OrdinalIgnoreCase);
        if (settings?.Vendor is not { } vendors) {
            return profiles;
        }
        foreach (var (vendor, profile) in vendors) {
            profiles[vendor] = ProfileEdit.From(profile);
        }
        return profiles;
    }

    /// <summary>按当前引擎配置构建命名空间视图（describe / mutate 共用）</summary>
    private async Task<DshSettingsNamespaceView> BuildViewAsync(CancellationToken cancellationToken) {
        var profiles = await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        var value = new DshProviderProfilesValue();
        foreach (var (vendor, profile) in profiles.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            value.Profiles[vendor] = ToValue(profile);
        }
        var element = JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshProviderProfilesValue);
        return new DshSettingsNamespaceView {
            Ns = Namespace,
            AutoGenerate = false,
            Schema = Schema,
            Value = element,
            User = element,
            Base = element,
            Revision = NamespaceRevision,
        };
    }

    /// <summary>引擎 profile → 前端字段（只投影模型页可编辑的连接字段与模型清单）</summary>
    private static DshProviderProfileValue ToValue(ProfileEdit profile) => new() {
        BaseUrl = profile.Endpoint,
        ApiKeyEnv = profile.ApiKeyEnvVar,
        Model = profile.Model,
        Models = [.. profile.Models.Select(ToModelValue)],
    };

    /// <summary>引擎模型项 → 模型页行（maxTokens 引擎无此字段，恒省略）</summary>
    private static DshProviderModelValue ToModelValue(ModelItemConfig model) => new() {
        Id = model.Id,
        Name = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName,
        ContextWindow = model.ContextWindow > 0 ? model.ContextWindow : null,
        InputModalities = ToInputs(model.Capabilities?.Modalities ?? ModelModalityKind.Text),
    };

    /// <summary>模态位 → 模型页输入类型（text/image；无已知位时按文本）</summary>
    private static List<string> ToInputs(ModelModalityKind modalities) {
        var inputs = new List<string>(2);
        if (modalities.HasFlag(ModelModalityKind.Text)) {
            inputs.Add("text");
        }
        if (modalities.HasFlag(ModelModalityKind.ReadImage)) {
            inputs.Add("image");
        }
        if (inputs.Count == 0) {
            inputs.Add("text");
        }
        return inputs;
    }

    /// <summary>
    /// 应用一条路径操作。<paramref name="op"/> 的路径必须是 <c>profiles/{供应商}/{字段}</c>，
    /// 未知供应商/字段与非法值都返回引导文案（说明收到什么、允许什么、下一步怎么写）。
    /// </summary>
    private static string? Apply(Dictionary<string, ProfileEdit> profiles, DshSettingsOp op, string settingsHint) {
        if (op.Path is not { Count: 3 } path || !string.Equals(path[0], "profiles", StringComparison.Ordinal)) {
            return $"settings/mutate 的 llm-deepseek 路径必须是 profiles/{{供应商}}/{{字段}}，字段可取 {string.Join("、", EditableFields)}；"
                + $"收到 path=[{string.Join('/', op.Path ?? [])}]。";
        }
        var vendor = path[1];
        if (!profiles.TryGetValue(vendor, out var profile)) {
            return profiles.Count == 0
                ? $"供应商 {vendor} 不在引擎配置中：{settingsHint} 的 vendor 节点为空。请先在 TUI 或 CLI 完成供应商配置，再回模型页编辑。"
                : $"供应商 {vendor} 不在引擎配置中；可编辑的供应商：{string.Join("、", profiles.Keys)}。请在模型页选择已有供应商，或先在 TUI/CLI 中新增供应商。";
        }
        var field = path[2];
        if (!EditableFields.Contains(field, StringComparer.Ordinal)) {
            return $"字段 {field} 不可在模型页编辑（模型页只改连接字段，协议与供应商身份归引擎配置）；"
                + $"可编辑字段：{string.Join("、", EditableFields)}。例如 set profiles/{vendor}/baseURL、set profiles/{vendor}/models；"
                + $"协议/供应商身份请改 providers_llm/{vendor}/config.json。";
        }
        return op.Op switch {
            "set" => SetValue(profile, field, op.Value, vendor),
            "unset" => UnsetValue(profile, field),
            _ => $"不支持的 op={op.Op}；settings/mutate 只接受 set / unset。清除某字段用 unset（路径如 profiles/{vendor}/baseURL）。",
        };
    }

    /// <summary>set 操作的字段值校验与写入</summary>
    private static string? SetValue(ProfileEdit profile, string field, JsonElement? value, string vendor) {
        if (value is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
            return $"set profiles/{vendor}/{field} 缺少 value；请传字符串（models 传模型对象数组），例如 "
                + "{\"op\":\"set\",\"path\":[\"profiles\",\"deepseek\",\"baseURL\"],\"value\":\"https://api.deepseek.com/v1\"}。";
        }
        switch (field) {
            case "baseURL":
                if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } baseUrl || string.IsNullOrWhiteSpace(baseUrl)) {
                    return $"profiles/{vendor}/baseURL 必须是非空字符串（OpenAI 兼容端点，如 https://api.deepseek.com/v1）；收到 {DescribeValue(element)}。";
                }
                profile.Endpoint = baseUrl.Trim();
                return null;
            case "apiKeyEnv":
                if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } keyEnv || string.IsNullOrWhiteSpace(keyEnv)) {
                    return $"profiles/{vendor}/apiKeyEnv 必须是非空字符串（凭据引用名，如 DEEPSEEK_API_KEY）；收到 {DescribeValue(element)}。密钥明文请走 credentials/set。";
                }
                profile.ApiKeyEnvVar = keyEnv.Trim();
                return null;
            case "model":
                if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } modelId || string.IsNullOrWhiteSpace(modelId)) {
                    return $"profiles/{vendor}/model 必须是非空字符串（默认模型 ID）；收到 {DescribeValue(element)}。可用模型见 profiles/{vendor}/models。";
                }
                profile.Model = modelId.Trim();
                return null;
            case "models":
                if (element.ValueKind != JsonValueKind.Array) {
                    return $"profiles/{vendor}/models 必须是模型对象数组（每项 {{id, name?, contextWindow?, maxTokens?, inputModalities?}}）；收到 {element.ValueKind}。";
                }
                return SetModels(profile, element, vendor);
            default:
                return $"字段 {field} 不可在模型页编辑；可编辑字段：{string.Join("、", EditableFields)}。";
        }
    }

    /// <summary>模型数组写入（按 id 合并保留引擎侧隐藏字段；重复/非法行给出明确引导）</summary>
    private static string? SetModels(ProfileEdit profile, JsonElement element, string vendor) {
        List<DshProviderModelValue>? incoming;
        try {
            incoming = element.Deserialize(DshJsonContext.Default.ListDshProviderModelValue);
        } catch (JsonException ex) {
            return $"profiles/{vendor}/models 无法解析为模型对象数组：{ex.Message}；每项至少要有字符串 id 字段（例如 deepseek-chat）。";
        }
        if (incoming is null) {
            profile.Models = [];
            return null;
        }
        var existing = profile.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ModelItemConfig>(incoming.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < incoming.Count; index++) {
            var model = incoming[index];
            if (string.IsNullOrWhiteSpace(model.Id)) {
                return $"models[{index}].id 不能为空；每行必须填模型 ID（如 deepseek-chat）。";
            }
            var id = model.Id.Trim();
            if (!seen.Add(id)) {
                return $"models[{index}].id 与前面的行重复（{id}）；模型 ID 必须唯一，请删除重复行。";
            }
            if (model.Name is { } name && string.IsNullOrWhiteSpace(name)) {
                return $"models[{index}].name 不能是空白字符串；不需要自定义显示名就清空该字段。";
            }
            if (model.ContextWindow is { } contextWindow && contextWindow <= 0) {
                return $"models[{index}].contextWindow 必须是正整数（token 数）；收到 {contextWindow}。";
            }
            if (model.MaxTokens is { } maxTokens) {
                return $"models[{index}].maxTokens 无法写入引擎：providers_llm/{vendor}/models.json 的模型项没有 maxTokens 字段"
                    + $"（最大输出 token 由供应商决定）；收到 {maxTokens}。请清空该字段后保存，其余字段会正常落盘。";
            }
            existing.TryGetValue(id, out var prior);
            if (ToModalities(model.InputModalities.Count > 0 ? model.InputModalities : null, prior, index) is not { } modalities) {
                return $"models[{index}].inputModalities 只支持 text / image 两个值；收到 [{string.Join("、", model.InputModalities)}]。";
            }
            var displayName = model.Name is { } display ? display.Trim() : "";
            resolved.Add(prior is null
                ? new ModelItemConfig {
                    Id = id,
                    DisplayName = displayName,
                    ContextWindow = model.ContextWindow ?? 0,
                    Capabilities = new ModelCapabilitiesConfig { Modalities = modalities },
                }
                : prior with {
                    Id = id,
                    DisplayName = displayName,
                    ContextWindow = model.ContextWindow ?? prior.ContextWindow,
                    Capabilities = (prior.Capabilities ?? new ModelCapabilitiesConfig()) with { Modalities = modalities },
                });
        }
        profile.Models = resolved;
        return null;
    }

    /// <summary>
    /// 模型页两态输入 → 引擎模态位：保留引擎其他模态位（不在模型页暴露的读取/生成能力不丢），
    /// 未提供输入类型时沿用原模型能力（新模型按文本）。
    /// </summary>
    private static ModelModalityKind? ToModalities(List<string>? inputs, ModelItemConfig? prior, int index) {
        if (inputs is null) {
            return prior?.Capabilities?.Modalities ?? ModelModalityKind.Text;
        }
        var owned = ModelModalityKind.None;
        foreach (var input in inputs) {
            switch (input) {
                case "text":
                    owned |= ModelModalityKind.Text;
                    break;
                case "image":
                    owned |= ModelModalityKind.ReadImage;
                    break;
                default:
                    return null;
            }
        }
        if (owned == ModelModalityKind.None) {
            owned = ModelModalityKind.Text;
        }
        var preserved = (prior?.Capabilities?.Modalities ?? ModelModalityKind.None) & ~InputOwnedFlags;
        return preserved | owned;
    }

    /// <summary>unset 操作：清空对应连接字段（models 清空模型清单）</summary>
    private static string? UnsetValue(ProfileEdit profile, string field) {
        switch (field) {
            case "baseURL":
                profile.Endpoint = null;
                break;
            case "apiKeyEnv":
                profile.ApiKeyEnvVar = null;
                break;
            case "model":
                profile.Model = null;
                break;
            case "models":
                profile.Models = [];
                break;
        }
        return null;
    }

    /// <summary>写回供应商子文件（config.json 连接字段 + models.json 模型清单）</summary>
    private async Task PersistAsync(string vendor, ProfileEdit profile, CancellationToken cancellationToken) {
        await SettingsLoader.SaveVendorConfigAsync(_fs, _baseDir, vendor, profile.ToConfig(), cancellationToken).ConfigureAwait(false);
        await SettingsLoader.SaveVendorModelsAsync(_fs, _baseDir, vendor, profile.Models, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>值的人类可读描述（错误消息用）</summary>
    private static string DescribeValue(JsonElement? value) => value is { } element
        ? element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ValueKind.ToString()
        : "unknown";

    /// <summary>收到的 args 摘要（截断，拒绝提示里指认入参）</summary>
    private static string Describe(JsonElement args) {
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
            return "(空)";
        }
        var raw = args.GetRawText();
        return raw.Length <= 200 ? raw : raw[..200] + "…";
    }

    /// <summary>供应商 profile 的可变编辑副本（只改模型页字段，其余原样保留）</summary>
    private sealed class ProfileEdit {
        /// <summary>供应商身份（原样保留）</summary>
        internal string? Provider { get; set; }
        /// <summary>协议（原样保留，模型页不改）</summary>
        internal string? Protocol { get; set; }
        /// <summary>默认模型</summary>
        internal string? Model { get; set; }
        /// <summary>API 端点</summary>
        internal string? Endpoint { get; set; }
        /// <summary>凭据引用名</summary>
        internal string? ApiKeyEnvVar { get; set; }
        /// <summary>模型列表拉取端点（原样保留）</summary>
        internal string? ModelsEndpoint { get; set; }
        /// <summary>模型清单</summary>
        internal List<ModelItemConfig> Models { get; set; } = [];

        /// <summary>从引擎 profile 复制可编辑状态</summary>
        internal static ProfileEdit From(ProfileSettings profile) => new() {
            Provider = profile.Provider,
            Protocol = profile.Protocol,
            Model = profile.Model,
            Endpoint = profile.Endpoint,
            ApiKeyEnvVar = profile.ApiKeyEnvVar,
            ModelsEndpoint = profile.ModelsEndpoint,
            Models = [.. profile.Models ?? []],
        };

        /// <summary>投影成 config.json 的 DTO（模型列表单独写 models.json）</summary>
        internal VendorConfigFile ToConfig() => new() {
            Provider = Provider,
            Protocol = Protocol,
            Model = Model,
            Endpoint = Endpoint,
            ApiKeyEnvVar = ApiKeyEnvVar,
            ModelsEndpoint = ModelsEndpoint,
        };
    }

    /// <summary>
    /// <c>llm-deepseek</c> 命名空间 schema 信封：root(dict profiles → object profile →
    /// baseURL/apiKeyEnv/model + models 数组)。前端 ConfigForm 需要它解析 settingsPath 与默认值。
    /// </summary>
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(
        new DshSchemaEnvelope {
            Uid = 8,
            Refs = new Dictionary<string, DshSchemaNode>(StringComparer.Ordinal) {
                ["1"] = new() { Type = "string" },
                ["2"] = new() { Type = "number" },
                ["3"] = new() { Type = "array", Inner = 1 },
                ["4"] = new() { Type = "array", Inner = 5 },
                ["5"] = new() {
                    Type = "object",
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) {
                        ["id"] = 1,
                        ["name"] = 1,
                        ["contextWindow"] = 2,
                        ["maxTokens"] = 2,
                        ["inputModalities"] = 3,
                    },
                },
                ["6"] = new() {
                    Type = "object",
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) {
                        ["baseURL"] = 1,
                        ["apiKeyEnv"] = 1,
                        ["model"] = 1,
                        ["models"] = 4,
                    },
                },
                ["7"] = new() { Type = "dict", Inner = 6 },
                ["8"] = new() {
                    Type = "object",
                    Meta = new DshSchemaMeta { Default = new DshSchemaEmptyObject() },
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) { ["profiles"] = 7 },
                },
            },
        },
        DshJsonContext.Default.DshSchemaEnvelope);
}
