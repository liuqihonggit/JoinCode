namespace JoinCode.CliCommands;

/// <summary>
/// 工具参数绑定器 — 统一「命令行 → 工具参数」的契约。
/// <para><b>分层理由</b>：参数「哪些合法、哪些必填」的真相在工具注册表的
/// <see cref="ToolSchema"/> 里。CLI 层不猜参数名，只做语法解析 —— 把
/// <c>--参数 值</c> / <c>key=value</c> / JSON 三种来源归一成同一张参数字典；
/// 语义校验放到宿主内对着参数表做，这样报错能列出该工具的真实参数名与相近建议，
/// 而不是「未知选项」这种没有上下文的拒绝（无上下文的拒绝会让调用方换个命令重试，反而更危险）。</para>
/// <para><b>为什么判定未知参数前要先归一化</b>：工具分发中间件链里的
/// <c>ArgumentRepairMiddleware</c> 已经用同一套别名表把 <c>filePath</c> 归一成
/// <c>file_path</c>；CLI 若不做同样的归一化，就会把合法写法误判成未知参数。</para>
/// </summary>
internal static class McpToolArgumentBinder {
    /// <summary>
    /// 从 argv 中抽取 <c>--参数 值</c>（或 <c>--参数=值</c>）形式的工具参数。
    /// <para>保留选项（<paramref name="reservedOptions"/>，即元命令自己的选项）原样留在
    /// 返回的 remaining 里，<b>并连同其值一起保留</b> —— 位置参数收集逻辑依赖
    /// 「选项紧跟其值」的相邻关系，拆开会让选项值被误当成位置参数。</para>
    /// <para>没有值的选项按布尔 <c>true</c> 处理（CLI 惯例）；随后由参数表类型判定是否成立，
    /// 类型不符会给出明确提示，而不是猜。</para>
    /// </summary>
    /// <param name="args">完整命令行。</param>
    /// <param name="startIndex">从第几个 token 开始扫描（之前的原样保留）。</param>
    /// <param name="reservedOptions">元命令自己的选项名，不当作工具参数。</param>
    /// <param name="reservedBooleanFlags">其中不带值的布尔标志。</param>
    /// <returns>工具参数表与剩余 argv。</returns>
    internal static (Dictionary<string, string> FlagArgs, string[] Remaining) ExtractFlagArguments(
        string[] args, int startIndex, FrozenSet<string> reservedOptions, FrozenSet<string> reservedBooleanFlags) {
        var flagArgs = new Dictionary<string, string>(StringComparer.Ordinal);
        var remaining = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++) {
            if (i < startIndex) {
                remaining.Add(args[i]);
                continue;
            }
            var token = args[i];
            if (!token.StartsWith("--")) {
                remaining.Add(token);
                continue;
            }
            if (reservedOptions.Contains(token)) {
                remaining.Add(token);
                if (ConsumesValue(args, i, reservedBooleanFlags)) {
                    remaining.Add(args[i + 1]);
                    i++;
                }
                continue;
            }
            CaptureFlagArgument(args, ref i, flagArgs, remaining);
        }
        return (flagArgs, remaining.ToArray());
    }

    /// <summary>
    /// 处理一个工具参数 token — 支持 <c>--参数=值</c> 与 <c>--参数 值</c> 两种写法。
    /// </summary>
    private static void CaptureFlagArgument(
        string[] args, ref int index, Dictionary<string, string> flagArgs, List<string> remaining) {
        var token = args[index];
        var eq = token.IndexOf('=');
        if (eq > 2) {
            flagArgs[token[2..eq]] = token[(eq + 1)..];
            return;
        }
        var name = token[2..];
        if (name.Length == 0) {
            remaining.Add(token);
            return;
        }
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--")) {
            flagArgs[name] = args[index + 1];
            index++;
            return;
        }
        flagArgs[name] = "true";
    }

    /// <summary>选项是否吞掉下一个 token 作为其值 — 布尔标志不吞。</summary>
    private static bool ConsumesValue(string[] args, int i, FrozenSet<string> booleanFlags) {
        if (booleanFlags.Contains(args[i])) return false;
        if (i + 1 >= args.Length) return false;
        return !args[i + 1].StartsWith("--");
    }

    /// <summary>
    /// 按工具参数表校验参数名与必填项 — 返回错误描述，通过则返回 null。
    /// <para>参数名先经 <see cref="LlmJsonHelper.RepairArguments"/> 归一化（与工具分发中间件
    /// 同一套别名表），归一化后仍不在参数表里的才算未知参数。</para>
    /// <para>错误描述是<b>单行</b>纯文本：它会被塞进 JSON 的 error.message，
    /// 多行的位置指示线在 JSON 里反而更难读。</para>
    /// </summary>
    /// <param name="args">待校验的参数表。</param>
    /// <param name="schema">工具参数表（唯一数据源）。</param>
    /// <param name="toolName">工具名（用于错误提示）。</param>
    /// <returns>错误描述；通过返回 null。</returns>
    internal static string? ValidateAgainstSchema(
        Dictionary<string, JsonElement> args, ToolSchema schema, string toolName) {
        var effective = LlmJsonHelper.RepairArguments(toolName, args, schema).RepairedArguments;
        foreach (var key in effective.Keys) {
            if (schema.Properties.ContainsKey(key)) continue;
            var error = Cli.Output.CliErrorCatalog.ArgUnknownToolParameter(
                toolName, key, BuildUnknownParameterHint(toolName, key, schema));
            return $"{error.Message}。{error.Hint}";
        }
        var missing = schema.Required.Where(name => !effective.ContainsKey(name)).ToArray();
        if (missing.Length == 0) return null;
        var missingError = Cli.Output.CliErrorCatalog.ArgMissingToolParameter(toolName, string.Join(", ", missing));
        return $"{missingError.Message}。{missingError.Hint}";
    }

    /// <summary>未知参数的修复提示 — 列出真实参数名 + 最相近的一个 + 查看入口。</summary>
    private static string BuildUnknownParameterHint(string toolName, string key, ToolSchema schema) {
        var available = schema.Properties.Keys.ToArray();
        var suggestions = LlmJsonHelper.SuggestToolNames(key, available);
        var listed = available.Length == 0 ? "(无)" : "--" + string.Join(", --", available);
        var hint = $"工具 '{toolName}' 的可用参数: {listed}";
        // 只点出最相近的那一个：把全部参数再列一遍没有信息量，反而盖住重点
        if (suggestions.Count > 0) hint += $"; 是不是想写 --{suggestions[0]}?";
        return hint + $"。用 jcc mcp_schema {toolName} 查看完整参数表";
    }
}
