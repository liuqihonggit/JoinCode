namespace JoinCode.Abstractions.Utils;

/// <summary>
/// JSON 修复管道 — 从 ToolCallRepairService 提取的单一职责小类
/// <para>职责: 多阶段 JSON 字符串修复(BOM/分号/引号/换行/逗号/key/value/转义/浮点/十六进制/截断)</para>
/// </summary>
internal static class JsonRepairPipeline {
    /// <summary>
    /// 修复 LLM 生成的非法 JSON — 多阶段管道修复
    /// <para>阶段: BOM剥离→分号剥离→外层引号剥离→快速解析→逐项修复→截断修复→shell剥引号修复</para>
    /// </summary>
    public static ToolCallRepairResult RepairJson(string? rawJson) {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new ToolCallRepairResult { Success = true, RepairedJson = "{}" };

        var json = StripBom(rawJson.Trim());
        json = StripTrailingSemicolon(json);
        json = StripOuterQuotes(json);

        if (TryParseJson(json, out _))
            return new ToolCallRepairResult { Success = true, RepairedJson = json };

        var hints = new List<string>();
        var repaired = json;

        repaired = FixRawNewlines(repaired, hints);
        repaired = RemoveTrailingCommas(repaired, hints);
        repaired = FixUnquotedKeys(repaired, hints);
        repaired = FixNamedFloatingPointLiterals(repaired, hints);
        repaired = FixUnquotedValues(repaired, hints);
        repaired = FixSingleQuotedStrings(repaired, hints);
        repaired = FixEscapeSequences(repaired, hints);
        repaired = FixHexAndLeadingZeroNumbers(repaired, hints);

        if (TryParseJson(repaired, out _))
            return new ToolCallRepairResult {
                Success = true,
                RepairedJson = repaired,
                RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
            };

        repaired = RepairTruncatedJson(repaired, hints);

        if (TryParseJson(repaired, out _))
            return new ToolCallRepairResult {
                Success = true,
                RepairedJson = repaired,
                RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
            };

        var shellStripped = RepairShellStrippedSingleKey(json, hints);
        if (shellStripped is not null && TryParseJson(shellStripped, out _))
            return new ToolCallRepairResult {
                Success = true,
                RepairedJson = shellStripped,
                RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
            };

        return new ToolCallRepairResult {
            Success = false,
            RepairedJson = repaired,
            RepairHint = $"JSON repair failed. Original: {TruncateForHint(rawJson)}"
        };
    }

    /// <summary>
    /// 激进修复 PowerShell 剥引号后的单键对象 — 值中含 {} 时 FixUnquotedValues 会截断
    /// <para>检测: {key:value} 无双引号、单键(key 不含逗号)</para>
    /// <para>策略: 第一个 : 前为 key,最后一个 } 前为 value,整体加引号</para>
    /// <para>返回 null 表示不适用(多键对象或已有引号)</para>
    /// </summary>
    private static string? RepairShellStrippedSingleKey(string json, List<string> hints) {
        if (json.Length < 4 || json[0] != '{' || json[^1] != '}')
            return null;
        if (!json.Contains(':') || json.Contains('"'))
            return null;

        var colonIdx = json.IndexOf(':');
        if (colonIdx <= 1)
            return null;

        var keySpan = json.AsSpan(1, colonIdx - 1).Trim();
        if (keySpan.Length == 0 || keySpan.Contains(','))
            return null;

        var valueSpan = json.AsSpan(colonIdx + 1, json.Length - colonIdx - 2).Trim();
        if (valueSpan.Length == 0)
            return null;

        var sb = new StringBuilder(json.Length + 8);
        sb.Append('"');
        sb.Append(keySpan);
        sb.Append("\":\"");
        for (var i = 0; i < valueSpan.Length; i++) {
            if (valueSpan[i] == '\\')
                sb.Append("\\\\");
            else if (valueSpan[i] == '"')
                sb.Append("\\\"");
            else
                sb.Append(valueSpan[i]);
        }
        sb.Append('"');

        hints.Add($"shell-stripped single-key repair (key={keySpan.ToString()})");
        return string.Concat("{", sb.ToString(), "}");
    }

    private static bool TryParseJson(string json, out JsonDocument? doc) {
        try {
            doc = JsonDocument.Parse(json);
            return true;
        } catch (JsonException) {
            doc = null;
            return false;
        }
    }

    /// <summary>
    /// 剥离 UTF-8/UTF-16 BOM 头
    /// </summary>
    private static string StripBom(string input) {
        var span = input.AsSpan();
        while (span.Length > 0 && (span[0] == '\uFEFF' || span[0] == '\uFFFE' || span[0] == '\u0000'))
            span = span[1..];

        return span.ToString();
    }

    private static string RemoveTrailingCommas(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == ',') {
                var j = i + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                if (j < json.Length && (json[j] == '}' || json[j] == ']')) {
                    changed = true;
                    i++;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("removed trailing comma(s)");

        return result.ToString();
    }

    private static string FixUnquotedKeys(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == '{' || json[i] == ',') {
                result.Append(json[i]);
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) { result.Append(json[i]); i++; }

                if (i < json.Length && json[i] == '"') {
                    continue;
                }

                if (i < json.Length && (char.IsLetter(json[i]) || json[i] == '_')) {
                    var keyStart = i;
                    while (i < json.Length && (char.IsLetterOrDigit(json[i]) || json[i] == '_')) i++;

                    var j = i;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                    if (j < json.Length && json[j] == ':') {
                        result.Append('"');
                        result.Append(json.AsSpan(keyStart, i - keyStart));
                        result.Append('"');
                        changed = true;
                        continue;
                    }
                }

                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("added quotes to unquoted key(s)");

        return result.ToString();
    }

    /// <summary>
    /// 给无引号的 JSON string value 加引号 — :value, → :"value",  :value} → :"value"}
    /// <para>跳过数字、true/false/null、嵌套对象{}和数组[]、已加引号的字符串</para>
    /// <para>字符级遍历，正确跳过字符串内的冒号，不会误处理字符串内的 :value, 模式</para>
    /// </summary>
    private static string FixUnquotedValues(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        // 将 json[start..end] 加双引号后追加到 result,裸反斜杠转义为 \\ (JSON 合法)
        static void AppendQuotedValue(StringBuilder sb, string s, int start, int end) {
            sb.Append('"');
            var span = s.AsSpan(start, end - start);
            for (var k = 0; k < span.Length; k++) {
                if (span[k] == '\\')
                    sb.Append("\\\\");
                else
                    sb.Append(span[k]);
            }
            sb.Append('"');
        }

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == '\'') {
                var start = i;
                i++;
                while (i < json.Length && json[i] != '\'') i++;
                if (i < json.Length) i++;
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == ':') {
                result.Append(json[i]);
                i++;

                while (i < json.Length && char.IsWhiteSpace(json[i])) { result.Append(json[i]); i++; }
                if (i >= json.Length) continue;

                var c = json[i];
                if (c == '"' || c == '\'' || c == '{' || c == '[') continue;
                if (char.IsDigit(c) || c == '-' || c == '+') continue;
                if (IsLiteralAt(json, i, "true") || IsLiteralAt(json, i, "false") || IsLiteralAt(json, i, "null"))
                    continue;

                var valueStart = i;
                // 保守收集: 到空格/逗号/}/] 停(值不含空格的快速路径)
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && !char.IsWhiteSpace(json[i]))
                    i++;

                if (i > valueStart) {
                    var j = i;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && (json[j] == ',' || json[j] == '}' || json[j] == ']')) {
                        AppendQuotedValue(result, json, valueStart, i);
                        changed = true;
                        continue;
                    }
                }

                // 保守收集失败(值含空格,如 PowerShell 剥引号后的 {prompt:echo hello})
                // → 激进收集: 允许空格,到 ,/}/]/{/[ 停,整体加引号
                i = valueStart;
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && json[i] != '{' && json[i] != '[')
                    i++;

                // 去掉尾部空白(避免 "echo hello " 带尾部空格在引号内)
                var valueEnd = i;
                while (valueEnd > valueStart && char.IsWhiteSpace(json[valueEnd - 1])) valueEnd--;

                if (valueEnd > valueStart) {
                    AppendQuotedValue(result, json, valueStart, valueEnd);
                    // 尾部空白在引号外原样输出
                    for (var k = valueEnd; k < i; k++)
                        result.Append(json[k]);
                    changed = true;
                    continue;
                }

                // 激进收集也失败(值为空或首字符即分隔符),原样输出
                result.Append(json.AsSpan(valueStart, i - valueStart));
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("added quotes to unquoted value(s)");

        return result.ToString();
    }

    /// <summary>
    /// 检查字符串指定位置是否匹配某个字面量（true/false/null），且后面是单词边界
    /// </summary>
    private static bool IsLiteralAt(string s, int index, string literal) {
        if (index + literal.Length > s.Length) return false;
        for (var k = 0; k < literal.Length; k++) {
            if (char.ToLowerInvariant(s[index + k]) != literal[k]) return false;
        }
        if (index + literal.Length < s.Length) {
            var next = s[index + literal.Length];
            if (char.IsLetterOrDigit(next) || next == '_') return false;
        }
        return true;
    }

    private static string StripTrailingSemicolon(string json) {
        var trimmed = json.AsSpan().Trim();
        if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == ';') {
            var end = json.Length;
            while (end > 0 && char.IsWhiteSpace(json[end - 1])) end--;
            if (end > 0 && json[end - 1] == ';')
                return json[..(end - 1)];
        }

        return json;
    }

    /// <summary>
    /// 去除 JSON 外层多余引号（单引号或双引号）— Shell 转义常见问题
    /// <para>如 '"{"key":"value"}"' → '{"key":"value"}'</para>
    /// <para>幂等：如果去除后不是合法 JSON 开头，保留原始引号</para>
    /// </summary>
    private static string StripOuterQuotes(string json) {
        if (json.Length < 2)
            return json;

        var first = json[0];
        var last = json[^1];
        if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
            var inner = json[1..^1];
            if (inner.Length > 0 && (inner[0] == '{' || inner[0] == '['))
                return inner;
        }

        return json;
    }

    /// <summary>
    /// 修复裸换行符和制表符 — Shell 转义常见问题
    /// <para>PowerShell 双引号字符串中 \n 被解释为实际换行符 → 替换回 \n 转义序列</para>
    /// <para>实际换行符在 JSON 字符串值中是非法的，必须替换为 \n 转义序列</para>
    /// <para>幂等：已转义的 \\n 不受影响（它是两个字符 \ 和 n，不是裸换行符）</para>
    /// </summary>
    private static string FixRawNewlines(string json, List<string> hints) {
        if (!json.Contains('\r') && !json.Contains('\n') && !json.Contains('\t'))
            return json;

        var repaired = json.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n").Replace("\t", "\\t");
        hints.Add("replaced raw newline/tab with escape sequences");
        return repaired;
    }

    private static string FixSingleQuotedStrings(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == '\'') {
                var contentStart = i + 1;
                var contentEnd = contentStart;
                while (contentEnd < json.Length && json[contentEnd] != '\'') contentEnd++;

                if (contentEnd < json.Length) {
                    result.Append('"');
                    result.Append(json.AsSpan(contentStart, contentEnd - contentStart));
                    result.Append('"');
                    changed = true;
                    i = contentEnd + 1;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("converted single-quoted string(s) to double quotes");

        return result.ToString();
    }

    /// <summary>
    /// 修复十六进制数字（0xFF → 255）与前导零数字（0123 → 123）。
    /// 字符串内容不受影响；对已是合法 JSON 的输入不产生任何改动。
    /// </summary>
    /// <summary>
    /// 修复 JSON 字符串中的非法转义序列和裸控制字符。
    /// 1. \' → ' （标准 JSON 字符串内不需要转义单引号，System.Text.Json 会拒绝）
    /// 2. 裸控制字符（0x00-0x1F）→ 对应 \n \t \r 等转义序列
    /// 3. 无效反斜杠转义（如 \p \w \R）→ 双写反斜杠（\\p \\w \\R），处理 Windows 路径
    /// </summary>
    private static string FixEscapeSequences(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) {
                        var next = json[i + 1];
                        if (next == '\'') {
                            result.Append(json.AsSpan(start, i - start));
                            result.Append('\'');
                            changed = true;
                            i += 2;
                            start = i;
                            continue;
                        }

                        if (next is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u')) {
                            result.Append(json.AsSpan(start, i - start));
                            result.Append("\\\\");
                            changed = true;
                            i++;
                            start = i;
                            continue;
                        }

                        i += 2;
                        continue;
                    }

                    if (json[i] == '"') { i++; break; }

                    if (json[i] < 0x20) {
                        result.Append(json.AsSpan(start, i - start));
                        result.Append(json[i] switch {
                            '\n' => "\\n",
                            '\r' => "\\r",
                            '\t' => "\\t",
                            '\b' => "\\b",
                            '\f' => "\\f",
                            _ => $"\\u{((int)json[i]):x4}"
                        });
                        changed = true;
                        i++;
                        start = i;
                        continue;
                    }

                    i++;
                }

                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("fixed escape sequence(s)/control character(s)");

        return result.ToString();
    }

    /// <summary>
    /// 修复 JSON 中的 Infinity / -Infinity / NaN 字面量。
    /// 标准 JSON 不支持这些值，System.Text.Json 默认拒绝。
    /// 策略：将裸字面量转为字符串（如 Infinity → "Infinity"），
    /// 由 JsonLenientCoercer 在类型转换层进一步处理。
    /// </summary>
    private static string FixNamedFloatingPointLiterals(string json, List<string> hints) {
        var changed = false;
        var literals = new List<string>();
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (i + 7 < json.Length && json.AsSpan(i, 8) is "Infinity") {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 8;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after)) {
                    result.Append("\"Infinity\"");
                    changed = true;
                    literals.Add("Infinity");
                    i += 8;
                    continue;
                }
            }

            if (i + 8 < json.Length && json.AsSpan(i, 9) is "-Infinity") {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 9;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after)) {
                    result.Append("\"-Infinity\"");
                    changed = true;
                    literals.Add("-Infinity");
                    i += 9;
                    continue;
                }
            }

            if (i + 2 < json.Length && json.AsSpan(i, 3) is "NaN") {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 3;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after)) {
                    result.Append("\"NaN\"");
                    changed = true;
                    literals.Add("NaN");
                    i += 3;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add($"quoted named floating-point literal(s): {string.Join(", ", literals)}");

        return result.ToString();
    }

    private static bool IsAlphaNumeric(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    private static string FixHexAndLeadingZeroNumbers(string json, List<string> hints) {
        var changed = false;
        var result = new StringBuilder(json.Length);
        var i = 0;

        while (i < json.Length) {
            if (json[i] == '"') {
                var start = i;
                i++;
                while (i < json.Length) {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            // 十六进制：0x / 0X 后跟十六进制数字（仅在数字 token 起点触发，避免 206 中的 0x 误匹配）
            if (json[i] == '0' && i + 1 < json.Length && (json[i + 1] == 'x' || json[i + 1] == 'X')
                && (i == 0 || !IsAlphaNumeric(json[i - 1]))) {
                var j = i + 2;
                var hexStart = j;
                while (j < json.Length && IsHexDigit(json[j])) j++;

                if (j > hexStart
                    && ulong.TryParse(json.AsSpan(hexStart, j - hexStart), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var hexVal)) {
                    result.Append(hexVal.ToString(CultureInfo.InvariantCulture));
                    changed = true;
                    i = j;
                    continue;
                }
            }

            // 前导零整数：0 紧跟数字（如 0123）→ 去前导零（保留至少一位）
            // 仅在数字 token 起点触发（前一个字符非字母数字），避免 206 中的 06 被误判为前导零
            if (json[i] == '0' && i + 1 < json.Length && json[i + 1] is >= '0' and <= '9'
                && (i == 0 || !IsAlphaNumeric(json[i - 1]))) {
                var j = i + 1;
                while (j < json.Length && json[j] is >= '0' and <= '9') j++;

                var k = i;
                while (k < j - 1 && json[k] == '0') k++;

                result.Append(json.AsSpan(k, j - k));
                changed = true;
                i = j;
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("converted hex/leading-zero number(s)");

        return result.ToString();
    }

    private static bool IsHexDigit(char c) {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static string TruncateForHint(string text, int maxLength = 200) {
        if (text.Length <= maxLength) return text;
        return $"{text[..(maxLength / 2)]}...{text[^(maxLength / 2)..]}";
    }

    private static string RepairTruncatedJson(string json, List<string> hints) {
        var sb = new StringBuilder(json.Length + 16);
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;

        for (var i = 0; i < json.Length; i++) {
            var c = json[i];

            if (escape) {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\' && inString) {
                sb.Append(c);
                escape = true;
                continue;
            }

            if (c == '"') {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString) {
                sb.Append(c);
                continue;
            }

            if (c is '{' or '[') {
                stack.Push(c);
                sb.Append(c);
                continue;
            }

            if (c is '}' or ']') {
                if (stack.Count > 0)
                    stack.Pop();
                sb.Append(c);
                continue;
            }

            sb.Append(c);
        }

        var modified = inString || stack.Count > 0 || sb.Length != json.Length;

        if (inString) {
            sb.Append('"');
            hints.Add("closed truncated string");
        }

        while (stack.Count > 0) {
            var opener = stack.Pop();
            var closer = opener == '{' ? '}' : ']';

            var len = sb.Length;
            while (len > 0 && char.IsWhiteSpace(sb[len - 1])) len--;
            if (len > 0 && sb[len - 1] == ',')
                sb.Length = len - 1;

            sb.Append(closer);
            modified = true;
        }

        if (!modified)
            return json;

        if (sb.Length > json.Length)
            hints.Add("closed truncated JSON structure");

        return sb.ToString();
    }
}