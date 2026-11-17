using System;
using System.Collections.Generic;
using System.Linq;

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
    /// 解析 profiles: 下的分组顺序表 — 键是 profile 名，值是注册类名列表。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseProfiles(string? yaml) {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (yaml is null || yaml.Length == 0) return result;

        string? profile = null;
        var names = new List<string>();
        foreach (var raw in yaml.Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("profiles:", StringComparison.Ordinal)) continue;

            if (!line.StartsWith("- ", StringComparison.Ordinal)) {
                if (profile is not null) result[profile] = names.ToArray();
                profile = line.TrimEnd(':').Trim();
                names = new List<string>();
                continue;
            }

            if (profile is null) continue;
            var name = line.Substring(2).Trim().Trim('"');
            if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }
        if (profile is not null) result[profile] = names.ToArray();
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
