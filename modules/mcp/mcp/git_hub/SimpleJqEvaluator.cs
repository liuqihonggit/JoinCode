namespace McpToolDispatch;

/// <summary>
/// 简易 jq 表达式求值器 — 支持 jq 子集用于 gh api --jq 参数（缺陷1b）。
/// <para>支持语法: .field, .field.sub, [], select(.field=="value" or ...), {key: .field}, | 管道</para>
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
            if (next is null) continue;
            node = next;
        }

        return node.ToJsonString();
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
            var s when s.Contains(' ') => null,
            _ => ApplyPath(input, stage),
        };

    /// <summary>.field 或 .field.sub 或 .field[] 或 .field[].sub</summary>
    private static JsonNode? ApplyPath(JsonNode? input, string path) {
        var remaining = path.AsSpan().Trim();
        if (remaining.Length > 0 && remaining[0] == '.')
            remaining = remaining[1..];
        if (remaining.IsEmpty) return input;

        var current = input;
        while (!remaining.IsEmpty) {
            var dotIdx = remaining.IndexOf('.');
            var part = dotIdx < 0 ? remaining : remaining[..dotIdx];
            remaining = dotIdx < 0 ? default : remaining[(dotIdx + 1)..];
            if (part.IsEmpty) continue;

            var isArrayExpand = part.Length >= 2 && part[^2] == '[' && part[^1] == ']';
            var fieldName = isArrayExpand ? part[..^2] : part;
            if (!fieldName.IsEmpty)
                current = GetFieldOrMapArray(current, fieldName.ToString());
            if (current is null)
                return null;
            if (isArrayExpand && current is not JsonArray)
                return null;
        }
        return current;
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

    /// <summary>.field=="value" or .field2=="value2" — 支持 or 逻辑</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateCondition(JsonNode? node, string condition) {
        var span = condition.AsSpan();
        while (!span.IsEmpty) {
            var orIdx = span.IndexOf(" or ");
            var part = orIdx < 0 ? span : span[..orIdx];
            span = orIdx < 0 ? default : span[(orIdx + 4)..];
            if (part.IsEmpty) continue;
            if (EvaluateComparison(node, part.Trim().ToString())) return true;
        }
        return false;
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
            if (colonIdx < 0) continue;
            var key = pair[..colonIdx].Trim().Trim('"').ToString();
            var valuePath = pair[(colonIdx + 1)..].Trim().ToString();
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
