
/// <summary>
/// 生成器共享的「显式顺序表」解析器 — 从 AdditionalFiles 里的 YAML 读注册名顺序。
/// <para>存在理由：把排序数字写在特性上，等于让反射/源码生成器去收集一堆散落的数字，
/// 顺序变化要改代码、还不容易一眼看出全貌。改成注册名 + YAML 后，顺序是可审查的单一清单。</para>
/// <para>这里只解析本项目约定的极简形状（<c>sections:</c> / <c>modules:</c> 下的 <c>- 名称</c> 列表），
/// 不引入 YamlDotNet —— 生成器是 netstandard2.0，额外依赖会拖慢每个消费工程的编译。</para>
/// </summary>
public static class OrderYaml {
    /// <summary>解析 YAML 中的顺序列表；重复名称只保留第一次，注释与空行忽略。</summary>
    public static IReadOnlyList<string> ParseNames(string? yaml) {
        var result = new List<string>();
        if (yaml is null || yaml.Length == 0) return result;

        foreach (var raw in yaml.Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
            var name = line.Substring(2).Trim().Trim('"');
            if (name.Length > 0 && !result.Contains(name, StringComparer.Ordinal))
                result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// 解析 profiles: 下的模块装配表 — 键是 profile 名，值是「归属程序集 + 模块注册名顺序」。
    /// <para>形状：<c>profiles: → &lt;profile&gt;: → assembly: &lt;程序集名&gt; / modules: → - 名称</c>。
    /// 归属程序集是必需的：CLI 引用引擎，同一个编译里既有本程序集模块也有引用来的模块，
    /// 没有归属信息就分不清「名字属于另一个程序集」与「名字打错了」。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ModuleProfile> ParseProfiles(string? yaml) {
        var result = new Dictionary<string, ModuleProfile>(StringComparer.OrdinalIgnoreCase);
        if (yaml is null || yaml.Length == 0) return result;

        string? profile = null;
        string? assembly = null;
        var modules = new List<string>();
        var inProfiles = false;
        var inModuleList = false;

        void Flush() {
            if (profile is not null)
                result[profile] = new ModuleProfile(assembly ?? string.Empty, modules.ToArray());
        }

        foreach (var raw in yaml.Split('\n')) {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            var indent = raw.Length - raw.TrimStart().Length;
            if (indent == 0) {
                // 顶层键：profiles: 开启分组段，其它顶层键结束它
                inProfiles = trimmed.StartsWith("profiles:", StringComparison.Ordinal);
                if (inProfiles) Flush();
                continue;
            }
            if (!inProfiles) continue;

            if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                if (!inModuleList || profile is null) continue;
                var name = trimmed.Substring(2).Trim().Trim('"');
                if (name.Length > 0 && !modules.Contains(name, StringComparer.Ordinal)) modules.Add(name);
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon < 0) continue;
            var key = trimmed.Substring(0, colon).Trim();
            var value = trimmed.Substring(colon + 1).Trim().Trim('"');

            if (key == "assembly") {
                assembly = value;
                continue;
            }
            if (key == "modules") {
                inModuleList = true;
                continue;
            }

            // 新的 profile 键
            Flush();
            profile = key;
            assembly = null;
            modules = new List<string>();
            inModuleList = false;
        }
        Flush();
        return result;
    }

    /// <summary>返回注册名在顺序表中的位置；未登记返回 int.MaxValue，由调用方报错。</summary>
    public static int IndexOf(IReadOnlyList<string> names, string name) {
        for (var i = 0; i < names.Count; i++) {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }
}

/// <summary>一个模块装配 profile 的声明 — 归属程序集 + 模块注册名顺序。</summary>
public sealed class ModuleProfile {
    public ModuleProfile(string assembly, IReadOnlyList<string> modules) {
        Assembly = assembly;
        Modules = modules;
    }

    /// <summary>归属程序集名（与 MSBuild 的 AssemblyName 一致）。</summary>
    public string Assembly { get; }

    /// <summary>模块注册名，顺序即装配顺序。</summary>
    public IReadOnlyList<string> Modules { get; }
}
