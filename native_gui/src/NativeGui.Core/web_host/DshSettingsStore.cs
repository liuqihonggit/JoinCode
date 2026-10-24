namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 设置命名空间存储（ADR 0143，TASK058 B3 持久化改造）。前端把「预览版说明」等确认状态写进
/// <c>ui-settings-general</c>，权限预设行写 <c>permission</c> 命名空间的 <c>defaultPreset</c>。
/// 值以「段路径 → JsonElement」扁平存储，读回时用 <see cref="Utf8JsonWriter"/> 还原成嵌套对象树
/// （不手写 JSON 字符串，遵守 ADR 0133）。
/// 持久化：<c>~/.jcc/dsh-settings.json</c>（RelaxedJsonSerializer + JsonContext DTO，重启不丢，消除进程内假数据）；
/// <c>permission/defaultPreset</c> 的值域以引擎 <see cref="PermissionMode"/> 枚举 <c>[EnumValue]</c> 词汇为唯一数据源，
/// 写入成功即触发 <see cref="PermissionPresetChanged"/>（宿主路由到引擎 SetPermissionModeAsync）。
/// </summary>
internal sealed class DshSettingsStore {
    /// <summary>前端「预览版说明」确认所在的命名空间</summary>
    private const string WelcomeNamespace = "ui-settings-general";

    /// <summary>权限预设命名空间（前端 ui-permission-presets 写 defaultPreset）</summary>
    private const string PermissionNamespace = "permission";

    /// <summary>开发者工具偏好命名空间（前端 ConfigForms.developerTools 读 enabled，TASK066 C7）</summary>
    private const string UiSettingsNamespace = "ui-settings";

    private readonly IFileSystem _fs;
    private readonly string _filePath;
    private readonly Lock Gate = new();
    private readonly Dictionary<string, NamespaceState> States = new(StringComparer.Ordinal) {
        [WelcomeNamespace] = new(),
        [UiSettingsNamespace] = new(),
    };
    private readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);
    private bool _loaded;

    /// <summary>defaultPreset 写入成功后触发（参数为引擎 PermissionMode 枚举值字符串）</summary>
    internal event Action<string>? PermissionPresetChanged;

    /// <summary>
    /// 打开设置文件的系统动作（默认交给 shell；测试替换为记录器）。
    /// 返回是否已启动以及失败说明（失败说明会进入 <c>settings/rejected</c> 消息）。
    /// </summary>
    internal Func<string, (bool Opened, string? Error)> OpenFile { get; set; } = OpenWithShell;

    /// <summary>设置文件路径（settings/describe.hasDocument 与 openSettingsDocument 的事实源）</summary>
    internal string FilePath => _filePath;

    /// <summary>
    /// 构造设置存储。
    /// </summary>
    /// <param name="fs">文件系统抽象（dsh-settings.json 持久化）</param>
    /// <param name="filePath">设置文件路径（默认 <c>~/.jcc/dsh-settings.json</c>）</param>
    internal DshSettingsStore(IFileSystem fs, string? filePath = null) {
        _fs = fs;
        _filePath = filePath ?? Path.Combine(AppDataPaths.Default.JccDirectory, "dsh-settings.json");
    }

    /// <summary>
    /// 启动期加载存档（幂等；文件缺失/损坏按空文档启动 — 引导错误不阻塞前端设置页）。
    /// permission 命名空间无存档值时以引擎 Auto 默认值播种。
    /// </summary>
    internal async Task InitializeAsync(CancellationToken cancellationToken = default) {
        if (!MarkLoaded()) {
            return;
        }
        DshSettingsDocument? document = null;
        try {
            if (_fs.FileExists(_filePath)) {
                var json = await _fs.ReadAllTextAsync(_filePath, cancellationToken);
                document = RelaxedJsonSerializer.Deserialize(json, DshJsonContext.Default.DshSettingsDocument);
            }
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-SETTINGS] load failed: {ex.Message}");
        }
        ApplyLoadedDocument(document);
    }

    /// <summary>标记已加载（幂等闸门；返回是否为本轮首次调用）</summary>
    private bool MarkLoaded() {
        lock (Gate) {
            if (_loaded) {
                return false;
            }
            _loaded = true;
            return true;
        }
    }

    /// <summary>把加载到的文档合并进命名空间状态（无文档仅播种 permission 默认值）</summary>
    private void ApplyLoadedDocument(DshSettingsDocument? document) {
        lock (Gate) {
            if (document is not null) {
                foreach (var (ns, saved) in document.Namespaces) {
                    var state = States.TryGetValue(ns, out var existing) ? existing : States[ns] = new NamespaceState();
                    state.Revision = saved.Revision;
                    foreach (var (path, value) in saved.Entries) {
                        state.Entries[path] = value;
                    }
                }
            }
            EnsureDefaults();
        }
    }

    /// <summary>settings/describe：列出全部命名空间视图</summary>
    internal DshSettingsDescribe Describe() {
        lock (Gate) {
            EnsureDefaults();
            return new DshSettingsDescribe {
                Writable = true,
                // 宿主始终持有可打开的 dsh-settings.json（首次打开前先落盘），通用设置页据此渲染「打开配置文件」
                HasDocument = true,
                Namespaces = [.. States
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => View(pair.Key, pair.Value))],
            };
        }
    }

    /// <summary>
    /// settings/openSettingsDocument：先把当前设置落盘（编辑器不会看到空文件），再交给系统默认程序打开。
    /// 启动失败返回带文件路径的引导，前端在「打开配置文件」按钮旁展示。
    /// </summary>
    internal DshRpcResult OpenDocument() {
        lock (Gate) {
            EnsureDefaults();
            Persist();
        }
        if (!_fs.FileExists(_filePath)) {
            return DshRpcResults.Fail(
                "settings/no-document",
                $"宿主无法写出设置文件 {_filePath}（目录不可写或文件系统拒绝写入）；请检查该路径的写权限后重试，或直接编辑同一路径。");
        }
        var (opened, error) = OpenFile(_filePath);
        if (!opened) {
            return DshRpcResults.Fail(
                "settings/rejected",
                $"{error ?? "settings/openSettingsDocument 无法启动系统默认程序"}。请在文件管理器中直接打开 {_filePath}。");
        }
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshSettingsDocumentOpenValue { Opened = true },
            DshJsonContext.Default.DshSettingsDocumentOpenValue));
    }

    /// <summary>默认打开方式：交给 shell 的默认程序（JSON 编辑器/记事本）</summary>
    private static (bool Opened, string? Error) OpenWithShell(string path) {
        try {
            using var process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return (true, null);
        } catch (Exception ex) {
            return (false, $"settings/openSettingsDocument 启动默认程序失败：{ex.Message}");
        }
    }

    /// <summary>settings/mutate：按路径应用 set/unset，返回该命名空间的新视图；成功后落盘</summary>
    internal DshRpcResult Mutate(JsonElement args) {
        DshSettingsMutateArgs? request;
        try {
            request = args.Deserialize(DshJsonContext.Default.DshSettingsMutateArgs);
        } catch (JsonException ex) {
            return DshRpcResults.Fail("settings/rejected", $"settings/mutate 参数无法解析：{ex.Message}");
        }
        if (request is null || string.IsNullOrEmpty(request.Ns)) {
            return DshRpcResults.Fail("settings/rejected", "settings/mutate 缺少命名空间 ns");
        }
        if (request.Ops is null || request.Ops.Count == 0) {
            return DshRpcResults.Fail("settings/rejected", $"settings/mutate 未提供 ops（ns={request.Ns}）");
        }
        string? routedPreset = null;
        lock (Gate) {
            EnsureDefaults();
            if (!States.TryGetValue(request.Ns, out var state)) {
                state = new NamespaceState();
                States[request.Ns] = state;
            }
            if (request.ExpectedRevision is { } expected && expected != state.Revision) {
                return DshRpcResults.Fail(
                    "settings/conflict",
                    $"settings/mutate 期望版本 {expected}，实际版本 {state.Revision}（ns={request.Ns}）");
            }
            foreach (var op in request.Ops) {
                if (!IsValidPermissionOp(request.Ns, op)) {
                    return DshRpcResults.Fail(
                        "settings/rejected",
                        $"permission/defaultPreset 的值必须是引擎枚举词汇（plan/auto/ask/bypass/unattended）之一；收到 {DescribeValue(op.Value)}");
                }
                if (!Apply(state, op)) {
                    return DshRpcResults.Fail(
                        "settings/rejected",
                        $"settings/mutate 无法应用操作 op={op.Op} path=[{string.Join('/', op.Path ?? [])}]（ns={request.Ns}）");
                }
            }
            state.Revision++;
            if (string.Equals(request.Ns, PermissionNamespace, StringComparison.Ordinal)
                && state.Entries.TryGetValue("defaultPreset", out var preset)
                && preset.ValueKind == JsonValueKind.String) {
                routedPreset = preset.GetString();
            }
            Persist();
            var view = View(request.Ns, state);
            if (routedPreset is not null) {
                PermissionPresetChanged?.Invoke(routedPreset);
            }
            return DshRpcResults.Ok(JsonSerializer.SerializeToElement(view, DshJsonContext.Default.DshSettingsNamespaceView));
        }
    }

    /// <summary>settings/replace：整段替换命名空间（section 即新值），返回新视图</summary>
    internal DshRpcResult Replace(JsonElement args) {
        DshSettingsReplaceArgs? request;
        try {
            request = args.Deserialize(DshJsonContext.Default.DshSettingsReplaceArgs);
        } catch (JsonException ex) {
            return DshRpcResults.Fail("settings/rejected", $"settings/replace 参数无法解析：{ex.Message}");
        }
        if (request is null || string.IsNullOrEmpty(request.Ns)) {
            return DshRpcResults.Fail("settings/rejected", "settings/replace 缺少命名空间 ns");
        }
        if (request.Section.ValueKind != JsonValueKind.Object) {
            return DshRpcResults.Fail("settings/rejected", $"settings/replace 的 section 必须是对象（ns={request.Ns}）");
        }
        return Commit(request.Ns, request.ExpectedRevision, "settings/replace", state => {
            if (ValidatePermissionValue(request.Ns, request.Section) is { } invalid) {
                return invalid;
            }
            state.Entries.Clear();
            Flatten(request.Section, "", state.Entries);
            return null;
        });
    }

    /// <summary>settings/update：按顶层字段补丁合并命名空间（null 删除字段），返回新视图</summary>
    internal DshRpcResult Update(JsonElement args) {
        DshSettingsUpdateArgs? request;
        try {
            request = args.Deserialize(DshJsonContext.Default.DshSettingsUpdateArgs);
        } catch (JsonException ex) {
            return DshRpcResults.Fail("settings/rejected", $"settings/update 参数无法解析：{ex.Message}");
        }
        if (request is null || string.IsNullOrEmpty(request.Ns)) {
            return DshRpcResults.Fail("settings/rejected", "settings/update 缺少命名空间 ns");
        }
        if (request.Patch.ValueKind != JsonValueKind.Object) {
            return DshRpcResults.Fail("settings/rejected", $"settings/update 的 patch 必须是对象（ns={request.Ns}）");
        }
        return Commit(request.Ns, request.ExpectedRevision, "settings/update", state => {
            if (ValidatePermissionValue(request.Ns, request.Patch) is { } invalid) {
                return invalid;
            }
            foreach (var field in request.Patch.EnumerateObject()) {
                RemoveSubtree(state.Entries, field.Name);
                if (field.Value.ValueKind != JsonValueKind.Null) {
                    Flatten(field.Value, field.Name, state.Entries);
                }
            }
            return null;
        });
    }

    /// <summary>
    /// 宿主命令路径静默写入 defaultPreset（TASK066 C7）：值未变化时不递增版本；不触发
    /// <see cref="PermissionPresetChanged"/>（调用方已完成引擎应用，避免回环重复应用）。
    /// </summary>
    /// <param name="preset">引擎枚举词汇（调用方已校验）</param>
    internal void SetPermissionPreset(string preset) {
        lock (Gate) {
            EnsureDefaults();
            var state = States[PermissionNamespace];
            if (state.Entries.TryGetValue("defaultPreset", out var current)
                && current.ValueKind == JsonValueKind.String
                && string.Equals(current.GetString(), preset, StringComparison.Ordinal)) {
                return;
            }
            state.Entries["defaultPreset"] = JsonSerializer.SerializeToElement(preset, DshJsonContext.Default.String);
            state.Revision++;
            Persist();
        }
    }

    /// <summary>读取命名空间某个扁平路径的字符串值（agentPresets/list 默认预设等）</summary>
    internal string? ReadString(string ns, params string[] path) {
        lock (Gate) {
            EnsureDefaults();
            return States.TryGetValue(ns, out var state)
                && state.Entries.TryGetValue(string.Join('/', path), out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }

    /// <summary>设置编辑的统一提交：版本校验 → 应用 → 递增版本 → 落盘 → 权限事件</summary>
    private DshRpcResult Commit(string ns, int? expectedRevision, string method, Func<NamespaceState, string?> apply) {
        lock (Gate) {
            EnsureDefaults();
            if (!States.TryGetValue(ns, out var state)) {
                state = new NamespaceState();
                States[ns] = state;
            }
            if (expectedRevision is { } expected && expected != state.Revision) {
                return DshRpcResults.Fail(
                    "settings/conflict",
                    $"{method} 期望版本 {expected}，实际版本 {state.Revision}（ns={ns}）");
            }
            if (apply(state) is { } error) {
                return DshRpcResults.Fail("settings/rejected", error);
            }
            if (string.Equals(ns, PermissionNamespace, StringComparison.Ordinal)) {
                EnsureDefaults();
            }
            state.Revision++;
            var routedPreset = ReadPermissionPreset(ns, state);
            Persist();
            var view = View(ns, state);
            if (routedPreset is not null) {
                PermissionPresetChanged?.Invoke(routedPreset);
            }
            return DshRpcResults.Ok(JsonSerializer.SerializeToElement(view, DshJsonContext.Default.DshSettingsNamespaceView));
        }
    }

    /// <summary>把对象树摊平成段路径 → 值（叶子含数组/标量；空对象不产生条目）</summary>
    private static void Flatten(JsonElement value, string prefix, Dictionary<string, JsonElement> entries) {
        if (value.ValueKind == JsonValueKind.Object) {
            foreach (var field in value.EnumerateObject()) {
                Flatten(field.Value, prefix.Length == 0 ? field.Name : prefix + "/" + field.Name, entries);
            }
            return;
        }
        entries[prefix] = value.Clone();
    }

    /// <summary>删除某字段及其全部子路径</summary>
    private static void RemoveSubtree(Dictionary<string, JsonElement> entries, string key) {
        var prefix = key + "/";
        foreach (var existing in entries.Keys
            .Where(candidate => candidate == key || candidate.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray()) {
            entries.Remove(existing);
        }
    }

    /// <summary>permission 命名空间 defaultPreset 的值域校验（replace/update 用；返回错误说明或 null）</summary>
    private static string? ValidatePermissionValue(string ns, JsonElement tree) {
        if (!string.Equals(ns, PermissionNamespace, StringComparison.Ordinal)
            || tree.ValueKind != JsonValueKind.Object
            || !tree.TryGetProperty("defaultPreset", out var preset)
            || preset.ValueKind != JsonValueKind.String
            || PermissionModeExtensions.FromValue(preset.GetString()) is not null) {
            return null;
        }
        return $"permission/defaultPreset 的值必须是引擎枚举词汇（plan/auto/ask/bypass/unattended）之一；收到 {preset.GetString()}";
    }

    /// <summary>权限命名空间当前 defaultPreset（非字符串/缺失时为 null）</summary>
    private static string? ReadPermissionPreset(string ns, NamespaceState state) =>
        string.Equals(ns, PermissionNamespace, StringComparison.Ordinal)
        && state.Entries.TryGetValue("defaultPreset", out var preset)
        && preset.ValueKind == JsonValueKind.String
            ? preset.GetString()
            : null;

    /// <summary>全量落盘（同步文件 IO — 设置文件 KB 级，前端 mutate 是低频交互；失败只记日志不回滚，下次写入覆盖）</summary>
    private void Persist() {
        try {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !_fs.DirectoryExists(directory)) {
                _fs.CreateDirectory(directory);
            }
            var document = new DshSettingsDocument {
                Namespaces = States.ToDictionary(
                    pair => pair.Key,
                    pair => new DshSettingsNamespaceDocument {
                        Revision = pair.Value.Revision,
                        Entries = pair.Value.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.Ordinal),
                    },
                    StringComparer.Ordinal),
            };
            var json = RelaxedJsonSerializer.Serialize(document, DshJsonContext.Default);
            _ = _fs.WriteAllText(_filePath, json);
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-SETTINGS] persist failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 已知命名空间播种：permission 无 defaultPreset 时写引擎 Auto 默认（与 permissionPresets/catalog 同源）；
    /// ui-settings 无 enabled 时按新安装默认开启（deliverables 改动卡片的前置开关）。
    /// </summary>
    private void EnsureDefaults() {
        var permission = States.TryGetValue(PermissionNamespace, out var existingPermission)
            ? existingPermission
            : States[PermissionNamespace] = new NamespaceState();
        if (!permission.Entries.ContainsKey("defaultPreset")) {
            permission.Entries["defaultPreset"] = JsonSerializer.SerializeToElement(
                PermissionMode.Auto.ToValue(), DshJsonContext.Default.String);
        }
        var uiSettings = States.TryGetValue(UiSettingsNamespace, out var existingUi)
            ? existingUi
            : States[UiSettingsNamespace] = new NamespaceState();
        if (!uiSettings.Entries.ContainsKey("enabled")) {
            uiSettings.Entries["enabled"] = JsonSerializer.SerializeToElement(true, DshJsonContext.Default.Boolean);
        }
    }

    /// <summary>单条操作：set 覆盖该路径，unset 删除该路径及其子树</summary>
    private static bool Apply(NamespaceState state, DshSettingsOp op) {
        if (op.Path is null || op.Path.Count == 0 || op.Path.Any(string.IsNullOrEmpty)) {
            return false;
        }
        var key = string.Join('/', op.Path);
        switch (op.Op) {
            case "set":
                if (op.Value is not { } value || value.ValueKind == JsonValueKind.Undefined) {
                    return false;
                }
                state.Entries[key] = value;
                return true;
            case "unset": {
                var prefix = key + "/";
                foreach (var existing in state.Entries.Keys
                    .Where(candidate => candidate == key || candidate.StartsWith(prefix, StringComparison.Ordinal))
                    .ToArray()) {
                    state.Entries.Remove(existing);
                }
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>permission 命名空间 defaultPreset 的 set 操作值域校验（引擎枚举词汇，fail-closed；其他命名空间/路径恒通过）</summary>
    private static bool IsValidPermissionOp(string ns, DshSettingsOp op) {
        if (!string.Equals(ns, PermissionNamespace, StringComparison.Ordinal)
            || !string.Equals(op.Op, "set", StringComparison.Ordinal)
            || op.Path?.Count != 1
            || !string.Equals(op.Path[0], "defaultPreset", StringComparison.Ordinal)) {
            return true;
        }
        return op.Value is { ValueKind: JsonValueKind.String } value
            && PermissionModeExtensions.FromValue(value.GetString()) is not null;
    }

    /// <summary>op 值的人类可读描述（错误消息用；缺失时字面 unknown）</summary>
    private static string DescribeValue(JsonElement? value) =>
        value is { } v ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind.ToString() : "unknown";

    /// <summary>命名空间视图（value/user 为同一棵树，base 暂为空；两个已知命名空间带 schema 信封）</summary>
    private DshSettingsNamespaceView View(string ns, NamespaceState state) {
        var tree = BuildTree(state.Entries);
        var known = KnownSchemaFor(ns);
        return new DshSettingsNamespaceView {
            Ns = ns,
            AutoGenerate = known is null,
            Revision = state.Revision,
            Value = tree,
            User = tree,
            Base = JsonNull,
            Schema = known ?? JsonNull,
            Secrets = [],
        };
    }

    /// <summary>已知命名空间的 schema 信封（ui-settings-general / permission / ui-settings — 前端 configForms 硬契约，缺省停在 loading）</summary>
    private JsonElement? KnownSchemaFor(string ns) => ns switch {
        WelcomeNamespace => WelcomeSchema,
        PermissionNamespace => PermissionSchema,
        UiSettingsNamespace => UiSettingsSchema,
        _ => null,
    };

    /// <summary>
    /// <c>ui-settings-general</c> 插件 Config 的 schema 信封，等价于前端插件里
    /// <c>volatileForm(z.object({ welcomeNoticeVersion: z.string().volatile() })).toJSON()</c>。
    /// </summary>
    private readonly JsonElement WelcomeSchema = JsonSerializer.SerializeToElement(
        new DshSchemaEnvelope {
            Uid = 4,
            Refs = new Dictionary<string, DshSchemaNode>(StringComparer.Ordinal) {
                ["3"] = new() { Type = "string" },
                ["4"] = new() {
                    Type = "object",
                    Meta = new DshSchemaMeta { Default = new DshSchemaEmptyObject() },
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) { ["welcomeNoticeVersion"] = 3 },
                },
            },
        },
        DshJsonContext.Default.DshSchemaEnvelope);

    /// <summary>
    /// <c>permission</c> 命名空间的 schema 信封 — 同构（defaultPreset: string volatile），
    /// 前端 ui-permission-presets 经 describeFace 消费 value.defaultPreset。
    /// </summary>
    private readonly JsonElement PermissionSchema = JsonSerializer.SerializeToElement(
        new DshSchemaEnvelope {
            Uid = 4,
            Refs = new Dictionary<string, DshSchemaNode>(StringComparer.Ordinal) {
                ["3"] = new() { Type = "string" },
                ["4"] = new() {
                    Type = "object",
                    Meta = new DshSchemaMeta { Default = new DshSchemaEmptyObject() },
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) { ["defaultPreset"] = 3 },
                },
            },
        },
        DshJsonContext.Default.DshSchemaEnvelope);

    /// <summary>
    /// <c>ui-settings</c> 命名空间的 schema 信封，等价于
    /// <c>Schema.object({ enabled: Schema.boolean().default(true) }).toJSON()</c>（开发者工具开关）。
    /// </summary>
    private readonly JsonElement UiSettingsSchema = JsonSerializer.SerializeToElement(
        new DshSchemaEnvelope {
            Uid = 4,
            Refs = new Dictionary<string, DshSchemaNode>(StringComparer.Ordinal) {
                ["3"] = new() { Type = "boolean" },
                ["4"] = new() {
                    Type = "object",
                    Meta = new DshSchemaMeta { Default = new DshSchemaEmptyObject() },
                    Dict = new Dictionary<string, int>(StringComparer.Ordinal) { ["enabled"] = 3 },
                },
            },
        },
        DshJsonContext.Default.DshSchemaEnvelope);

    /// <summary>扁平段路径集合 → 嵌套 JSON 对象（Utf8JsonWriter，AOT 安全）</summary>
    private static JsonElement BuildTree(Dictionary<string, JsonElement> entries) {
        var leaves = entries
            .Select(entry => (Segments: entry.Key.Split('/'), entry.Value))
            .ToArray();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            WriteLevel(writer, leaves, 0);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>递归写一层对象；同键既有叶子又有子树时以子树为准</summary>
    private static void WriteLevel(Utf8JsonWriter writer, (string[] Segments, JsonElement Value)[] entries, int depth) {
        writer.WriteStartObject();
        foreach (var group in entries.GroupBy(entry => entry.Segments[depth]).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            var branches = group.Where(entry => entry.Segments.Length > depth + 1).ToArray();
            writer.WritePropertyName(group.Key);
            if (branches.Length > 0) {
                WriteLevel(writer, branches, depth + 1);
                continue;
            }
            group.First().Value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    /// <summary>单命名空间状态</summary>
    private sealed class NamespaceState {
        /// <summary>段路径（以 / 连接）→ 值</summary>
        internal Dictionary<string, JsonElement> Entries { get; } = new(StringComparer.Ordinal);
        /// <summary>写入代数（每次成功 mutate +1，随文档持久化）</summary>
        internal int Revision { get; set; }
    }
}

/// <summary>宿主设置文档（dsh-settings.json 根）— 命名空间 → 扁平段路径树</summary>
internal sealed class DshSettingsDocument {
    /// <summary>命名空间存档</summary>
    public Dictionary<string, DshSettingsNamespaceDocument> Namespaces { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>单命名空间存档 — revision 与扁平段路径值</summary>
internal sealed class DshSettingsNamespaceDocument {
    /// <summary>写入代数（重启恢复，前端 expectedRevision 续用）</summary>
    public int Revision { get; set; }
    /// <summary>段路径（以 / 连接）→ 值</summary>
    public Dictionary<string, JsonElement> Entries { get; set; } = new(StringComparer.Ordinal);
}
