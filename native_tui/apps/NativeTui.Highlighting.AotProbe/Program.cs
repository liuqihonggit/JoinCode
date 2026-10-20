namespace NativeTui.Highlighting.AotProbe;

/// <summary>
/// AOT 探针主程序 — PublishAot 发布后运行，验证 TextMateSharp/Onigwrap 原生依赖链在 NativeAOT 下可用：
/// ① Onigwrap 原生 DLL 正确携带并 P/Invoke 成功（TextMate 语法用 oniguruma 正则）
/// ② TextMateSharp.Grammars 语法 JSON 嵌入资源在裁剪后可加载
/// ③ 逐行 tokenize + 主题色匹配出彩色 ANSI
/// 任一失败进程非零退出（CI 可用 exit code 判定）。
/// </summary>
public static class Program
{
    /// <summary>探针入口：高亮固定 C#/bash 样本，输出 token 色彩统计与 ANSI 样例。</summary>
    public static int Main()
    {
        try
        {
            var ansiDark = ProbeLanguage(
                "csharp",
                [
                    "// comment",
                    "using static System.Console;",
                    "var keyword = \"string literal\";",
                ]);
            var ansiBash = ProbeLanguage(
                "shellscript",
                [
                    "#!/bin/bash",
                    "echo \"hello\" | grep -q hello",
                ]);

            Console.WriteLine(ansiDark);
            Console.WriteLine(ansiBash);
            Console.WriteLine("AOTPROBE OK — TextMateSharp 语法/主题/Onigwrap 原生链路全部通过");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AOTPROBE FAIL: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>单语言探针：加载语法 → 逐行 tokenize → 主题取色 → ANSI 输出（校验 token 数与着色非空）。</summary>
    private static string ProbeLanguage(string languageId, IReadOnlyList<string> lines)
    {
        var registryOptions = new TextMateSharp.Grammars.RegistryOptions(TextMateSharp.Grammars.ThemeName.Monokai);
        var registry = new TextMateSharp.Registry.Registry(registryOptions);
        var scopeName = registryOptions.GetScopeByLanguageId(languageId)
            ?? throw new InvalidOperationException($"language id not found: {languageId}");
        var grammar = registry.LoadGrammar(scopeName)
            ?? throw new InvalidOperationException($"grammar not loaded: {scopeName}");
        var theme = registry.GetTheme();

        var builder = new System.Text.StringBuilder();
        TextMateSharp.Grammars.IStateStack? ruleStack = null;
        var coloredTokens = 0;
        foreach (var line in lines)
        {
            builder.Append("  ");
            var result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);
            ruleStack = result.RuleStack;
            foreach (var token in result.Tokens)
            {
                var start = Math.Min(token.StartIndex, line.Length);
                var end = Math.Min(token.EndIndex, line.Length);
                var text = line[start..end];
                var foreground = string.Empty;
                foreach (var rule in theme.Match(token.Scopes))
                {
                    if (rule.foreground >= 0)
                    {
                        foreground = theme.GetColor(rule.foreground);
                        break;
                    }
                }

                if (foreground.Length != 0)
                {
                    coloredTokens++;
                    builder.Append($"\x1b[38;2;{RgbFromHtml(foreground)}m{text}\x1b[0m");
                }
                else
                {
                    builder.Append(text);
                }
            }

            builder.AppendLine();
        }

        if (coloredTokens == 0)
        {
            throw new InvalidOperationException($"no colored tokens for {languageId} — theme matching broken");
        }

        return builder.ToString();
    }

    /// <summary>#RRGGBB → "r;g;b"（SGR truecolor 参数）。</summary>
    private static string RgbFromHtml(string htmlColor)
    {
        if (htmlColor.Length is 7 && htmlColor[0] == '#')
        {
            var r = Convert.ToInt32(htmlColor.Substring(1, 2), 16);
            var g = Convert.ToInt32(htmlColor.Substring(3, 2), 16);
            var b = Convert.ToInt32(htmlColor.Substring(5, 2), 16);
            return $"{r};{g};{b}";
        }

        return "255;255;255";
    }
}
