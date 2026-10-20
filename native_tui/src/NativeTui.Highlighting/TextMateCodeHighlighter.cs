namespace NativeTui.Highlighting;

/// <summary>
/// TextMateSharp 高亮器（M7-5 A 方案）— ICodeHighlighter 卫星实现：
/// 引擎 = TextMateSharp（VS Code 同源 TextMate 语法 + oniguruma 正则，基准 Rust 原版 syntect 同路线）；
/// 主题 dark=Monokai / light=LightPlus（对齐基准暗色 Monokai Extended / 亮色 GitHub-light 的近似项）；
/// 语法按需加载并缓存（scope → IGrammar，null 缓存负结果）；语言名解析走 Id/别名表（OrdinalIgnoreCase）。
/// Core 零依赖 — 本类型只经宿主组装注入（ReplShell 构造参数），不进入 Core 编译单元。
/// </summary>
public sealed class TextMateCodeHighlighter(bool dark) : ICodeHighlighter
{
    /// <summary>Registry（语法注册表 + 主题持有者 — 主构造参数 dark 选定 Monokai/LightPlus）。</summary>
    private readonly TextMateSharp.Registry.Registry _registry = new(new TextMateSharp.Grammars.RegistryOptions(
        dark ? TextMateSharp.Grammars.ThemeName.Monokai
             : TextMateSharp.Grammars.ThemeName.LightPlus));

    /// <summary>主题（scope → 颜色匹配器；Registry 内部缓存实例，取用零成本）。</summary>
    private TextMateSharp.Themes.Theme Theme => _registry.GetTheme();

    /// <summary>语法选项（语言解析入口 — Registry 持有的同一实例，取用零成本）。</summary>
    private TextMateSharp.Grammars.RegistryOptions Options =>
        (TextMateSharp.Grammars.RegistryOptions)_registry.GetLocator();

    /// <summary>语言名 → scope 缓存（含 null 负结果 — 未命中别名也缓存避免重复遍历）。</summary>
    private readonly Dictionary<string, string?> _scopeByAlias = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>scope → 语法缓存（含 null 负结果）。</summary>
    private readonly Dictionary<string, TextMateSharp.Grammars.IGrammar?> _grammars = new(StringComparer.Ordinal);

    /// <summary>语言名手动别名（Grammar 包 Aliases 未覆盖的常见写法 — bash/sh/zsh 族与 C 系缩写）。</summary>
    private static readonly IReadOnlyDictionary<string, string> ManualAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bash"] = "shellscript",
            ["sh"] = "shellscript",
            ["zsh"] = "shellscript",
            ["shell"] = "shellscript",
            ["cs"] = "csharp",
            ["c++"] = "cpp",
            ["js"] = "javascript",
            ["ts"] = "typescript",
            ["py"] = "python",
            ["rb"] = "ruby",
            ["rs"] = "rust",
            ["golang"] = "go",
            ["yml"] = "yaml",
            ["docker"] = "dockerfile",
            ["md"] = "markdown",
        };

    /// <summary>按语言整块高亮 — 每调用一个会话（ruleStack 逐行推进，跨行语法正确）；null = 语言不支持。</summary>
    public IReadOnlyList<IReadOnlyList<HighlightSpan>>? Highlight(string language, IReadOnlyList<string> lines)
    {
        var scopeName = ResolveScope(language);
        if (scopeName is null)
        {
            return null;
        }

        var grammar = LoadGrammar(scopeName);
        if (grammar is null)
        {
            return null;
        }

        var result = new IReadOnlyList<HighlightSpan>[lines.Count];
        TextMateSharp.Grammars.IStateStack? ruleStack = null;
        for (var index = 0; index < lines.Count; index++)
        {
            result[index] = HighlightSingleLine(grammar, lines[index], ruleStack, out ruleStack);
        }

        return result;
    }

    /// <summary>语言名 → scope（手动别名 → Id 精确 → Grammar 包 Name/Aliases 遍历；未命中缓存负结果）。</summary>
    private string? ResolveScope(string language)
    {
        var name = language.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (_scopeByAlias.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var scope = ResolveScopeUncached(name);
        _scopeByAlias[name] = scope;
        return scope;
    }

    /// <summary>未缓存解析路径 — 手动别名改写 → languageId 精确 → 可用语言 Id/Name/Aliases 遍历。</summary>
    private string? ResolveScopeUncached(string name)
    {
        if (ManualAliases.TryGetValue(name, out var aliased))
        {
            name = aliased;
        }

        var options = Options;
        var scope = options.GetScopeByLanguageId(name);
        if (scope is not null)
        {
            return scope;
        }

        return options.GetAvailableLanguages()
            .Where(lang => IdOrAliasMatches(lang, name))
            .Select(lang => options.GetScopeByLanguageId(lang.Id))
            .FirstOrDefault(resolved => resolved is not null);
    }

    /// <summary>语言 Id/Aliases 任一命中（OrdinalIgnoreCase）。</summary>
    private static bool IdOrAliasMatches(TextMateSharp.Grammars.Language language, string name) =>
        string.Equals(language.Id, name, StringComparison.OrdinalIgnoreCase)
            || (language.Aliases?.Contains(name, StringComparer.OrdinalIgnoreCase) ?? false);

    /// <summary>scope → 语法（缓存含负结果；加载失败返回 null）。</summary>
    private TextMateSharp.Grammars.IGrammar? LoadGrammar(string scopeName)
    {
        if (_grammars.TryGetValue(scopeName, out var cached))
        {
            return cached;
        }

        TextMateSharp.Grammars.IGrammar? grammar = null;
        try
        {
            grammar = _registry.LoadGrammar(scopeName);
        }
        catch (Exception)
        {
            grammar = null;
        }

        _grammars[scopeName] = grammar;
        return grammar;
    }

    /// <summary>单行 tokenize + 主题取色 → 色段列表（token 前景未设置 = 无色段，调用方用默认色）。</summary>
    private IReadOnlyList<HighlightSpan> HighlightSingleLine(
        TextMateSharp.Grammars.IGrammar grammar,
        string line,
        TextMateSharp.Grammars.IStateStack? prevState,
        out TextMateSharp.Grammars.IStateStack? nextState)
    {
        var tokens = grammar.TokenizeLine(line, prevState, TimeSpan.MaxValue);
        nextState = tokens.RuleStack;

        var spans = new List<HighlightSpan>();
        foreach (var token in tokens.Tokens)
        {
            var start = Math.Min(token.StartIndex, line.Length);
            var end = Math.Min(token.EndIndex, line.Length);
            if (end <= start)
            {
                continue;
            }

            var (r, g, b) = ForegroundColor(token.Scopes);
            if (r >= 0)
            {
                spans.Add(new HighlightSpan(start, end - start, r, g, b));
            }
        }

        return spans;
    }

    /// <summary>scopes → 前景 RGB（首个带前景的主题规则；-1 = 未设置回退默认色）。</summary>
    private (int R, int G, int B) ForegroundColor(IList<string> scopes)
    {
        foreach (var rule in Theme.Match(scopes))
        {
            if (rule.foreground >= 0 && TryParseHtmlColor(Theme.GetColor(rule.foreground), out var rgb))
            {
                return rgb;
            }
        }

        return (-1, -1, -1);
    }

    /// <summary>#RRGGBB → RGB 解析（非 7 位 # 形态回退 false）。</summary>
    private static bool TryParseHtmlColor(string color, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (color.Length is not 7 || color[0] != '#')
        {
            return false;
        }

        try
        {
            rgb = (
                Convert.ToInt32(color.Substring(1, 2), 16),
                Convert.ToInt32(color.Substring(3, 2), 16),
                Convert.ToInt32(color.Substring(5, 2), 16));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
