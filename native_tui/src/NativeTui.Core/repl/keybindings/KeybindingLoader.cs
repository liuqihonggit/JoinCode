namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位加载结果 — 对齐基准 loadUserBindings.ts KeybindingsLoadResult：
/// Profile 为尽力合并结果（错误条目跳过、合法条目生效、其余保留默认 — 把通过和错误都告诉调用方，
/// 由调用方决定展示策略）；Errors 为全量校验错误清单（空 = 完全合法或无用户文件）。
/// </summary>
/// <param name="Profile">合并后的绑定表。</param>
/// <param name="Errors">校验错误（语法/结构/语义/保留键）。</param>
public sealed record KeybindingLoadResult(KeybindingProfile Profile, IReadOnlyList<KeybindingError> Errors)
{
    /// <summary>是否存在任何错误。</summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// 用户键位加载器 — ~/.jcc/keybindings.json 覆盖默认表，对齐基准 loadUserBindings.ts + validate.ts 加载校验链：
/// 读文件 → DTO 反序列化（AOT JsonContext，宽容注释/尾逗号）→ 结构校验（未知 context/action/键名 → 收集
/// 四要素错误并跳过该条）→ 保留键校验（ReservedShortcuts）→ 合法条目替换合并（用户覆盖默认、其余保留默认）。
/// 合并语义：用户 (context, action) 的键位数组**整体替换**默认键位（空数组 = 解除绑定，对齐基准 null-unbind）；
/// 文件不存在 → 纯默认表无错误（对齐基准 ENOENT 静默回退语义）。
/// </summary>
public static class KeybindingLoader
{
    /// <summary>默认配置路径（~/.jcc/keybindings.json — 经 JccHome 跟随 JCC_APP_DATA_FOLDER 覆盖，测试隔离不污染真实用户目录）。</summary>
    public static string DefaultPath => JccHome.Combine("keybindings.json");

    /// <summary>加载默认路径用户键位（文件不存在 → 默认表 + 无错误）。</summary>
    /// <returns>加载结果。</returns>
    public static KeybindingLoadResult Load() => LoadFromFile(DefaultPath);

    /// <summary>加载指定路径用户键位（IO 失败 → 默认表 + IoRead 错误，不中断启动）。</summary>
    /// <param name="path">配置文件路径。</param>
    /// <returns>加载结果。</returns>
    public static KeybindingLoadResult LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            // 缺文件 = 未配置 — 静默回退默认表（对齐 HistorySeeder 坏文件降级先例，不算错误）
            return new KeybindingLoadResult(KeybindingProfile.Default, []);
        }
        catch (DirectoryNotFoundException)
        {
            return new KeybindingLoadResult(KeybindingProfile.Default, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new KeybindingLoadResult(KeybindingProfile.Default, [KeybindingError.IoRead(path, ex.Message)]);
        }

        return Parse(json);
    }

    /// <summary>
    /// 解析用户键位 JSON 文本（测试友好入口）：反序列化 → 校验收集错误 → 替换合并。
    /// 顶层结构非法（非对象/坏语法）→ 默认表 + JsonSyntax 错误（附行列）。
    /// </summary>
    /// <param name="json">用户键位 JSON 文本。</param>
    /// <returns>加载结果（Profile 尽力合并；Errors 全量报告）。</returns>
    public static KeybindingLoadResult Parse(string json)
    {
        Dictionary<string, Dictionary<string, List<string>>>? file;
        try
        {
            file = (Dictionary<string, Dictionary<string, List<string>>>?)JsonSerializer.Deserialize(
                json,
                typeof(Dictionary<string, Dictionary<string, List<string>>>),
                KeybindingJsonContext.Default);
        }
        catch (JsonException ex)
        {
            return new KeybindingLoadResult(KeybindingProfile.Default, [KeybindingError.JsonSyntax(ex)]);
        }

        return Merge(file ?? []);
    }

    /// <summary>校验用户块并合并默认表：合法条目替换、错误条目跳过（错误全量收集）。</summary>
    /// <param name="file">反序列化后的用户配置（context 名 → action 名 → 键位串数组）。</param>
    /// <returns>加载结果。</returns>
    private static KeybindingLoadResult Merge(Dictionary<string, Dictionary<string, List<string>>> file)
    {
        var errors = new List<KeybindingError>();
        var overrides = CollectOverrides(file, errors);
        return new KeybindingLoadResult(BuildProfile(overrides), errors);
    }

    /// <summary>校验并收集合法覆盖条目：未知 context 跳过整块、未知 action/坏键位跳过该条（错误入列表）。</summary>
    /// <param name="file">用户配置。</param>
    /// <param name="errors">错误收集（输出累积）。</param>
    /// <returns>(context, action) → 合法键序列列表（空列表 = 解除绑定）。</returns>
    private static Dictionary<(KeybindingContext, KeybindingAction), List<KeyChord>> CollectOverrides(
        Dictionary<string, Dictionary<string, List<string>>> file,
        List<KeybindingError> errors)
    {
        var overrides = new Dictionary<(KeybindingContext, KeybindingAction), List<KeyChord>>();
        foreach (var (contextName, actions) in file)
        {
            if (!KeybindingNames.TryResolveContext(contextName, out var context))
            {
                errors.Add(KeybindingError.UnknownContext(contextName));
                continue;
            }

            // json 值为 null（{"chat": null}）→ 视为空块容忍跳过（合法 context 无覆盖条目）
            if (actions is null)
            {
                continue;
            }

            foreach (var (actionName, keys) in actions)
            {
                if (!KeybindingNames.TryResolveAction(actionName, out var action))
                {
                    errors.Add(KeybindingError.UnknownAction(context, actionName));
                    continue;
                }

                // 显式空数组 = 解绑（记空 override 触发整体替换）；全部键位被拒（保留键/坏键）=
                // 无效条目非用户意图 — 跳过覆盖，默认键保留
                var parsed = ParseKeys(context, action, keys, errors);
                if (parsed.Count > 0 || keys is { Count: 0 })
                {
                    overrides[(context, action)] = parsed;
                }
            }
        }

        return overrides;
    }

    /// <summary>解析键位串数组：坏键名/保留键 → 错误并跳过该键位（其余键位仍生效 — 尽力合并）。</summary>
    /// <param name="context">所在上下文。</param>
    /// <param name="action">目标动作。</param>
    /// <param name="keys">用户键位串数组（可为 null — json 值非数组时）。</param>
    /// <param name="errors">错误收集。</param>
    /// <returns>合法键序列列表。</returns>
    private static List<KeyChord> ParseKeys(
        KeybindingContext context,
        KeybindingAction action,
        List<string>? keys,
        List<KeybindingError> errors)
    {
        var chords = new List<KeyChord>();
        foreach (var keyText in keys ?? [])
        {
            if (!KeyChord.TryParse(keyText, out var chord, out var error))
            {
                errors.Add(KeybindingError.UnknownKey(context, action, keyText, error));
                continue;
            }

            if (ReservedShortcuts.ReservedReason(chord!) is { } reason)
            {
                errors.Add(KeybindingError.ReservedKey(context, action, keyText, reason));
                continue;
            }

            chords.Add(chord!);
        }

        return chords;
    }

    /// <summary>默认行 + 用户覆盖合并 → profile：同 (context, action) 用户键列表整体替换默认键列表。</summary>
    /// <param name="overrides">合法用户覆盖条目。</param>
    /// <returns>合并后的 profile。</returns>
    private static KeybindingProfile BuildProfile(Dictionary<(KeybindingContext, KeybindingAction), List<KeyChord>> overrides)
    {
        var rows = KeybindingProfile.Default.Rows.ToList();
        rows.RemoveAll(row => overrides.ContainsKey((row.Context, row.Action)));
        rows.AddRange(overrides.SelectMany(pair => pair.Value.Select(chord => (pair.Key.Item1, pair.Key.Item2, chord))));
        return KeybindingProfile.Of(rows);
    }
}
