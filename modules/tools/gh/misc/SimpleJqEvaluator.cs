namespace McpToolDispatch;

/// <summary>
/// 简易 jq 表达式求值器 — 支持 jq 子集用于 gh api --jq 参数（缺陷1b）。
/// <para>支持语法: .field, .field.sub, [], select(.field=="value" or/and ...), {key: .field}, | 管道, length 函数</para>
/// <para>AOT 兼容: 仅用 JsonNode（无反射 emit），适合 NativeAOT 发布</para>
/// </summary>
internal static class SimpleJqEvaluator {
    /// <summary>
    /// 对 JSON 字符串执行 jq 表达式筛选，返回结果 JSON 字符串。
    /// 解析失败返回 null，调用方回退原始 JSON。
    /// </summary>
    internal static string? Evaluate(string json, string jqExpr) {
        var node = JsonNode.Parse(json);
        if (node is null) return null;

        var stages = SplitPipeline(jqExpr);
        foreach (var stage in stages) {
            var next = ApplyStage(node, stage);
            if (next is null) {
                if (IsSupportedStage(stage)) return "null";
                continue;
            }
            node = next;
        }

        return WriteNodeWithEncoder(node);
    }

    /// <summary>判断 stage 是否为支持的 jq 语法 — 用于区分"路径不存在"与"不支持的语法"</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedStage(string stage)
        => stage.StartsWith("select(", StringComparison.Ordinal)
           || stage.StartsWith('{')
           || stage == "length"
           || (stage.StartsWith('.') && !stage.Contains(' '));

    /// <summary>
    /// 用 UnsafeRelaxedJsonEscaping 序列化（不转义中文/emoji），
    /// 然后把 HTML 敏感字符的 \uXXXX 转义还原为原始字符（CLI 输出不需要 XSS 防护）。
    /// </summary>
    private static string WriteNodeWithEncoder(JsonNode node) {
        var json = node.ToJsonString(new JsonSerializerOptions {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        });
        return GhJsonContext.UnescapeHtmlChars(json);
    }

    /// <summary>按 | 分割管道阶段（忽略引号/括号内的 |）</summary>
    private static List<string> SplitPipeline(string expr) {
        var stages = new List<string>();
        var parenDepth = 0;
        var braceDepth = 0;
        var inString = false;
        var start = 0;
        for (var i = 0; i < expr.Length; i++) {
            var c = expr[i];
            if (c == '"') inString = !inString;
            else if (inString) continue;
            else if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == '{') braceDepth++;
            else if (c == '}') braceDepth--;
            else if (c == '|' && parenDepth == 0 && braceDepth == 0) {
                stages.Add(expr[start..i].Trim());
                start = i + 1;
            }
        }
        stages.Add(expr[start..].Trim());
        return stages;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyStage(JsonNode? input, string stage)
        => stage switch {
            var s when s.StartsWith("select(", StringComparison.Ordinal) => ApplySelect(input, s),
            var s when s.StartsWith('{') => ApplyObjectConstruct(input, s),
            "length" => ApplyLength(input),
            var s when s.StartsWith('.') && !s.Contains(' ') => ApplyPath(input, stage),
            _ => null,
        };

    /// <summary>length 函数 — 数组返回元素数，对象返回字段数，null 返回 0，标量返回 1</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyLength(JsonNode? node)
        => node switch {
            JsonArray arr => arr.Count,
            JsonObject obj => obj.Count,
            null => 0,
            _ => 1
        };

    /// <summary>
    /// 路径 token — 状态机的状态，由 ParsePathTokens 解析，由 ApplyToken 执行。
    /// <para>Field: .field 字段访问（对数组逐元素 map）</para>
    /// <para>ArrayExpand: .field[] 数组展开（对对象宽容展开常见数组字段）</para>
    /// <para>IndexAccess: .field[N] 索引访问（越界返回空数组 []）</para>
    /// </summary>
    private abstract record PathToken {
        public sealed record Field(string Name) : PathToken;
        public sealed record ArrayExpand() : PathToken;
        public sealed record IndexAccess(int Index) : PathToken;
    }

    /// <summary>
    /// 解析路径字符串为 token 序列（状态机转换函数）。
    /// <para>例: ".items[0].name" → [Field("items"), IndexAccess(0), Field("name")]</para>
    /// <para>例: ".[]" → [ArrayExpand()]</para>
    /// <para>例: ".workflow_runs[].id" → [Field("workflow_runs"), ArrayExpand(), Field("id")]</para>
    /// </summary>
    private static List<PathToken> ParsePathTokens(string path) {
        var remaining = path.AsSpan().Trim();
        if (remaining.Length > 0 && remaining[0] == '.')
            remaining = remaining[1..];
        var tokens = new List<PathToken>();
        while (!remaining.IsEmpty) {
            var dotIdx = remaining.IndexOf('.');
            var part = dotIdx < 0 ? remaining : remaining[..dotIdx];
            remaining = dotIdx < 0 ? default : remaining[(dotIdx + 1)..];
            if (part.IsEmpty) continue;
            AddPartTokens(tokens, part.ToString());
        }
        return tokens;
    }

    /// <summary>解析单个 part（如 items[]/items[0]/items[0][1]）为 token 序列 — 支持嵌套数组访问</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddPartTokens(List<PathToken> tokens, string part) {
        var bracketIdx = part.IndexOf('[');
        if (bracketIdx < 0) {
            if (!string.IsNullOrEmpty(part))
                tokens.Add(new PathToken.Field(part));
            return;
        }
        var fieldName = part[..bracketIdx];
        if (!string.IsNullOrEmpty(fieldName))
            tokens.Add(new PathToken.Field(fieldName));
        var remaining = part.AsSpan(bracketIdx);
        while (remaining.Length > 0 && remaining[0] == '[') {
            var closeIdx = remaining.IndexOf(']');
            if (closeIdx < 0) break;
            var content = remaining[1..closeIdx];
            if (content.IsEmpty)
                tokens.Add(new PathToken.ArrayExpand());
            else if (int.TryParse(content, out var index))
                tokens.Add(new PathToken.IndexAccess(index));
            remaining = remaining[(closeIdx + 1)..];
        }
    }

    /// <summary>.field 路径访问 — 对象取字段，数组逐元素 map</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyField(JsonNode? node, string name)
        => GetFieldOrMapArray(node, name);

    /// <summary>[] 数组展开 — 数组原样返回，对象宽容展开常见数组字段</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyArrayExpand(JsonNode? node)
        => node is JsonArray ? node : UnwrapCommonArrayField(node);

    /// <summary>[N] 索引访问 — 越界或非数组返回空数组 []（非 null，避免 Evaluate 跳过 stage）</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyIndexAccess(JsonNode? node, int index) {
        var arr = node as JsonArray ?? UnwrapCommonArrayField(node) as JsonArray;
        if (arr is null) return new JsonArray();
        return index >= 0 && index < arr.Count ? arr[index] : new JsonArray();
    }

    /// <summary>token 变换分派 — LINQ Aggregate 的单步函数</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? ApplyToken(JsonNode? node, PathToken token)
        => token switch {
            PathToken.Field f => ApplyField(node, f.Name),
            PathToken.ArrayExpand => ApplyArrayExpand(node),
            PathToken.IndexAccess i => ApplyIndexAccess(node, i.Index),
            _ => node
        };

    /// <summary>.field 或 .field.sub 或 .field[] 或 .field[].sub — LINQ Aggregate 链式执行</summary>
    private static JsonNode? ApplyPath(JsonNode? input, string path) {
        var tokens = ParsePathTokens(path);
        return tokens.Aggregate(input, (current, token) =>
            current is null ? null : ApplyToken(current, token));
    }

    /// <summary>从对象取字段，或对数组逐元素取字段（jq map 语义）</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? GetFieldOrMapArray(JsonNode? current, string fieldName) {
        if (current is JsonArray mapArr)
            return MapArrayField(mapArr, fieldName);
        if (current is JsonObject obj && obj.TryGetPropertyValue(fieldName, out var val))
            return val;
        return null;
    }

    /// <summary>对 JsonArray 逐元素取字段，返回新数组</summary>
    private static JsonArray MapArrayField(JsonArray arr, string fieldName) {
        var mapped = new JsonArray();
        foreach (var item in arr) {
            if (item is not JsonObject itemObj) continue;
            if (!itemObj.TryGetPropertyValue(fieldName, out var itemVal)) continue;
            var clone = itemVal?.DeepClone();
            mapped.Add(clone);
        }
        return mapped;
    }

    /// <summary>
    /// 宽容: [] 作用于对象时,自动查找常见数组字段展开。
    /// GitHub API 常用包装: {workflow_runs:[...]}, {items:[...]}, {data:[...]}, {runs:[...]}, {jobs:[...]} 等
    /// GraphQL 响应多层嵌套: {data:{repository:{discussions:{nodes:[...]}}}} — 递归查找(限深 4 层)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonNode? UnwrapCommonArrayField(JsonNode? current, int depth = 0) {
        if (current is not JsonObject obj || depth > 4) return null;
        foreach (var key in s_commonArrayFields) {
            if (obj.TryGetPropertyValue(key, out var val) && val is JsonArray arr)
                return arr;
        }
        foreach (var kvp in obj) {
            if (kvp.Value is JsonObject child) {
                var found = UnwrapCommonArrayField(child, depth + 1);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static readonly string[] s_commonArrayFields = [
        "workflow_runs", "items", "data", "runs", "jobs", "pulls", "issues",
        "repositories", "assets", "entries", "results", "records", "list",
        "branches", "tags", "releases", "labels", "milestones", "comments",
        "reviews", "commits", "files", "checks", "steps", "workflows",
        "secrets", "variables", "actions_caches", "rulesets", "codespaces",
        "discussions", "projects", "teams", "members", "collaborators",
        "contributors", "forks", "environments", "deployments", "statuses",
        "check_runs", "check_suites", "artifacts", "events", "reactions",
        "assignees", "gists", "keys", "ssh_keys", "gpg_keys", "topics",
        "hooks", "invitations", "installations", "permissions", "roles",
        "nodes", "edges", "contents"
    ];

    /// <summary>select(.field=="value" or .field2=="value2") — 过滤数组或单个对象</summary>
    private static JsonNode? ApplySelect(JsonNode? input, string stage) {
        var openParen = stage.IndexOf('(');
        var closeParen = stage.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0) return input;
        var condition = stage[(openParen + 1)..closeParen];

        if (input is JsonArray arr) {
            var filtered = new JsonArray();
            foreach (var item in arr) {
                if (EvaluateCondition(item, condition)) {
                    var clone = item?.DeepClone();
                    filtered.Add(clone);
                }
            }
            return filtered;
        }
        return EvaluateCondition(input, condition) ? input : null;
    }

    /// <summary>.field=="value" or .field2=="value2" — 支持 or/and 混合逻辑（and 优先级高于 or）</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateCondition(JsonNode? node, string condition) {
        var span = condition.AsSpan();
        while (!span.IsEmpty) {
            var orIdx = span.IndexOf(" or ");
            var part = orIdx < 0 ? span : span[..orIdx];
            span = orIdx < 0 ? default : span[(orIdx + 4)..];
            if (part.IsEmpty) continue;
            if (EvaluateAndParts(node, part.Trim().ToString())) return true;
        }
        return false;
    }

    /// <summary>and 子表达式 — 所有比较都满足才返回 true</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateAndParts(JsonNode? node, string condition) {
        var span = condition.AsSpan();
        while (!span.IsEmpty) {
            var andIdx = span.IndexOf(" and ");
            var part = andIdx < 0 ? span : span[..andIdx];
            span = andIdx < 0 ? default : span[(andIdx + 5)..];
            if (part.IsEmpty) continue;
            if (!EvaluateComparison(node, part.Trim().ToString())) return false;
        }
        return true;
    }

    /// <summary>.field=="value" 或 .field!="value"</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateComparison(JsonNode? node, string comparison) {
        var span = comparison.AsSpan();
        var neqIdx = span.IndexOf("!=");
        var eqIdx = span.IndexOf("==");
        var isNotEqual = neqIdx >= 0 && (eqIdx < 0 || neqIdx < eqIdx);
        var opIdx = isNotEqual ? neqIdx : eqIdx;
        if (opIdx < 0) return false;

        var left = span[..opIdx].Trim().ToString();
        var right = span[(opIdx + 2)..].Trim().Trim('"').ToString();

        var value = GetFieldValue(node, left);
        var strValue = value?.GetValue<string>() ?? value?.ToString();
        return isNotEqual ? strValue != right : strValue == right;
    }

    /// <summary>{key: .field, key2: .field2} — 对数组逐元素构造或对单个对象构造</summary>
    private static JsonNode? ApplyObjectConstruct(JsonNode? input, string stage) {
        var template = stage.Trim();
        if (template.Length < 2) return input;
        template = template[1..^1];

        if (input is JsonArray arr) {
            var result = new JsonArray();
            foreach (var item in arr) {
                var obj = (JsonNode)ConstructObject(item, template);
                result.Add(obj);
            }
            return result;
        }
        return ConstructObject(input, template);
    }

    private static JsonObject ConstructObject(JsonNode? input, string template) {
        var obj = new JsonObject();
        var span = template.AsSpan();
        while (!span.IsEmpty) {
            var commaIdx = span.IndexOf(',');
            var pair = commaIdx < 0 ? span : span[..commaIdx];
            span = commaIdx < 0 ? default : span[(commaIdx + 1)..];
            if (pair.IsEmpty) continue;
            var colonIdx = pair.IndexOf(':');
            string key;
            string valuePath;
            if (colonIdx < 0) {
                var fieldName = pair.Trim().Trim('"').ToString();
                key = fieldName;
                valuePath = "." + fieldName;
            } else {
                key = pair[..colonIdx].Trim().Trim('"').ToString();
                valuePath = pair[(colonIdx + 1)..].Trim().ToString();
            }
            obj[key] = GetFieldValue(input, valuePath)?.DeepClone();
        }
        return obj;
    }

    /// <summary>.field.sub → 从 node 中取嵌套字段值</summary>
    private static JsonNode? GetFieldValue(JsonNode? node, string path) {
        if (path == ".") return node;
        var remaining = path.AsSpan().Trim();
        if (remaining.Length > 0 && remaining[0] == '.')
            remaining = remaining[1..];
        if (remaining.IsEmpty) return node;

        var current = node;
        while (!remaining.IsEmpty) {
            var dotIdx = remaining.IndexOf('.');
            var part = dotIdx < 0 ? remaining : remaining[..dotIdx];
            remaining = dotIdx < 0 ? default : remaining[(dotIdx + 1)..];
            if (part.IsEmpty) continue;
            if (current is not JsonObject obj) return null;
            if (!obj.TryGetPropertyValue(part.ToString(), out current)) return null;
        }
        return current;
    }
}
