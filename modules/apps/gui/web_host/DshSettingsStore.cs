namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 设置命名空间存储（ADR 0143）。前端把「预览版说明」等确认状态写进
/// <c>ui-settings-general</c>，并立刻用 <c>settings/describe</c> 读回来判定成功，
/// 因此宿主必须真的落库并维护 revision。
/// 值以「段路径 → JsonElement」扁平存储，读回时用 <see cref="Utf8JsonWriter"/> 还原成嵌套对象树
/// （不手写 JSON 字符串，遵守 ADR 0133）。
/// 当前为进程内存储（M2 再接入自有引擎的设置持久化与真实 schema）。
/// </summary>
internal sealed class DshSettingsStore {
    /// <summary>前端「预览版说明」确认所在的命名空间</summary>
    private const string WelcomeNamespace = "ui-settings-general";

    private readonly Lock Gate = new();
    private readonly Dictionary<string, NamespaceState> States = new(StringComparer.Ordinal) {
        [WelcomeNamespace] = new(),
    };
    private readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);

    /// <summary>
    /// <c>ui-settings-general</c> 插件 Config 的 schema 信封，等价于前端插件里
    /// <c>volatileForm(z.object({ welcomeNoticeVersion: z.string().volatile() })).toJSON()</c>。
    /// 前端必须能还原并校验通过 <c>value</c>，否则命名空间停在 loading、提示页不渲染。
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

    /// <summary>单命名空间状态</summary>
    private sealed class NamespaceState {
        /// <summary>段路径（以 / 连接）→ 值</summary>
        internal Dictionary<string, JsonElement> Entries { get; } = new(StringComparer.Ordinal);
        /// <summary>写入代数（每次成功 mutate +1）</summary>
        internal int Revision { get; set; }
    }

    /// <summary>清空并重建为初始状态（测试与重新初始化用）</summary>
    internal void Reset() {
        lock (Gate) {
            States.Clear();
            States[WelcomeNamespace] = new NamespaceState();
        }
    }

    /// <summary>settings/describe：列出全部命名空间视图</summary>
    internal DshSettingsDescribe Describe() {
        lock (Gate) {
            return new DshSettingsDescribe {
                Writable = true,
                HasDocument = false,
                Namespaces = [.. States
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => View(pair.Key, pair.Value))],
            };
        }
    }

    /// <summary>settings/mutate：按路径应用 set/unset，返回该命名空间的新视图</summary>
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
        lock (Gate) {
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
                if (!Apply(state, op)) {
                    return DshRpcResults.Fail(
                        "settings/rejected",
                        $"settings/mutate 无法应用操作 op={op.Op} path=[{string.Join('/', op.Path ?? [])}]（ns={request.Ns}）");
                }
            }
            state.Revision++;
            return DshRpcResults.Ok(JsonSerializer.SerializeToElement(View(request.Ns, state), DshJsonContext.Default.DshSettingsNamespaceView));
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

    /// <summary>命名空间视图（value/user 为同一棵树，base 暂为空）</summary>
    private DshSettingsNamespaceView View(string ns, NamespaceState state) {
        var tree = BuildTree(state.Entries);
        var isWelcome = string.Equals(ns, WelcomeNamespace, StringComparison.Ordinal);
        return new DshSettingsNamespaceView {
            Ns = ns,
            AutoGenerate = !isWelcome,
            Revision = state.Revision,
            Value = tree,
            User = tree,
            Base = JsonNull,
            Schema = isWelcome ? WelcomeSchema : JsonNull,
            Secrets = [],
        };
    }

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
}
