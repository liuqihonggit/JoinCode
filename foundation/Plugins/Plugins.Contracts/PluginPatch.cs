namespace JoinCode.Abstractions.Entity;

/// <summary>
/// patch 条目 — 对齐 DSH cordis.patch.yml 行
/// <para>两种操作：insert（按 id 在目标追加子列表）/ 按 id 覆盖整行</para>
/// </summary>
public sealed class PluginPatchEntry
{
    /// <summary>条目 id（定位目标行）</summary>
    public required string Id { get; init; }

    /// <summary>name（覆盖时校验，不符才改）</summary>
    public string? Name { get; init; }

    /// <summary>config（整行替换，非深合并）</summary>
    public object? Config { get; init; }

    /// <summary>disabled 标记</summary>
    public bool? Disabled { get; init; }

    /// <summary>inject 声明</summary>
    public string[]? Inject { get; init; }

    /// <summary>group 归属</summary>
    public string? Group { get; init; }

    /// <summary>isolate 隔离标记</summary>
    public bool? Isolate { get; init; }

    /// <summary>intercept 拦截</summary>
    public string? Intercept { get; init; }

    /// <summary>insert 子列表（缩进子列表，按 id 在目标 group 追加；空表示非 insert 操作）</summary>
    public List<PluginPatchEntry> Insert { get; init; } = new();
}

/// <summary>
/// patch 层 — 对齐 DSH cordis.patch.yml 顶层数组
/// </summary>
public sealed class PluginPatch
{
    /// <summary>patch 条目列表</summary>
    public List<PluginPatchEntry> Entries { get; init; } = new();
}

/// <summary>
/// 配置行 — 被 patch 操作的目标
/// </summary>
public sealed class PluginConfigRow
{
    /// <summary>行 id</summary>
    public required string Id { get; set; }

    /// <summary>插件名</summary>
    public string? Name { get; set; }

    /// <summary>配置（整行替换）</summary>
    public object? Config { get; set; }

    /// <summary>是否禁用</summary>
    public bool Disabled { get; set; }

    /// <summary>inject 声明</summary>
    public string[]? Inject { get; set; }

    /// <summary>group 归属</summary>
    public string? Group { get; set; }

    /// <summary>isolate 隔离</summary>
    public bool Isolate { get; set; }

    /// <summary>intercept 拦截</summary>
    public string? Intercept { get; set; }
}

/// <summary>
/// patch 应用结果（含 warn）
/// </summary>
public sealed class PluginPatchApplyResult
{
    /// <summary>应用后的配置行</summary>
    public required List<PluginConfigRow> Rows { get; init; }

    /// <summary>警告列表（name 不符、id 找不到等）</summary>
    public required List<string> Warnings { get; init; }
}

/// <summary>
/// patch 应用器 — 多层拍平应用
/// <para>对齐 DSH applyEntryPatches：后层覆盖前层，一次性应用</para>
/// <para>三条约束：config 整行替换非深合并；name 不符 warn 后跳过；无 replace/ignore 动词</para>
/// </summary>
public static class PluginPatchApplicator
{
    /// <summary>
    /// 应用单层 patch 到配置行列表
    /// </summary>
    public static PluginPatchApplyResult Apply(List<PluginConfigRow> rows, PluginPatch patch)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(patch);

        var result = new List<PluginConfigRow>(rows);
        var warnings = new List<string>();
        var idIndex = new Dictionary<string, int>();
        for (int i = 0; i < result.Count; i++)
        {
            idIndex[result[i].Id] = i;
        }

        for (int i = 0; i < patch.Entries.Count; i++)
        {
            var entry = patch.Entries[i];
            idIndex.TryGetValue(entry.Id, out var idx);
            var found = idx >= 0 && idx < result.Count && result[idx].Id == entry.Id;

            if (entry.Insert.Count > 0)
            {
                var insertList = entry.Insert;
                if (!found)
                {
                    warnings.Add($"patch id '{entry.Id}' 找不到目标行，insert 跳过");
                    continue;
                }
                for (int j = 0; j < insertList.Count; j++)
                {
                    var insert = insertList[j];
                    var newRow = new PluginConfigRow
                    {
                        Id = insert.Id,
                        Name = insert.Name,
                        Config = insert.Config,
                        Inject = insert.Inject,
                        Group = insert.Group,
                        Isolate = insert.Isolate ?? false,
                        Intercept = insert.Intercept,
                    };
                    result.Add(newRow);
                    idIndex[insert.Id] = result.Count - 1;
                }
            }
            else if (found)
            {
                var target = result[idx];
                if (entry.Name is not null && target.Name is not null && entry.Name != target.Name)
                {
                    warnings.Add($"patch id '{entry.Id}' name '{entry.Name}' 与目标 '{target.Name}' 不符，跳过");
                    continue;
                }
                if (entry.Name is not null) target.Name = entry.Name;
                if (entry.Config is not null) target.Config = entry.Config;
                if (entry.Disabled is not null) target.Disabled = entry.Disabled.Value;
                if (entry.Inject is not null) target.Inject = entry.Inject;
                if (entry.Group is not null) target.Group = entry.Group;
                if (entry.Isolate is not null) target.Isolate = entry.Isolate.Value;
                if (entry.Intercept is not null) target.Intercept = entry.Intercept;
            }
            else
            {
                warnings.Add($"patch id '{entry.Id}' 找不到目标行，跳过");
            }
        }

        return new PluginPatchApplyResult { Rows = result, Warnings = warnings };
    }

    /// <summary>
    /// 多层拍平应用 — 按序应用多个 patch 层，后层覆盖前层
    /// <para>对齐 DSH 完整应用顺序：profile.bundles → profile patch → 全局 patch → overlays</para>
    /// </summary>
    public static PluginPatchApplyResult ApplyLayers(List<PluginConfigRow> baseRows, params PluginPatch[] layers)
    {
        ArgumentNullException.ThrowIfNull(baseRows);
        var rows = new List<PluginConfigRow>(baseRows);
        var allWarnings = new List<string>();
        for (int i = 0; i < layers.Length; i++)
        {
            var result = Apply(rows, layers[i]);
            rows = result.Rows;
            allWarnings.AddRange(result.Warnings);
        }
        return new PluginPatchApplyResult { Rows = rows, Warnings = allWarnings };
    }
}

/// <summary>
/// 插件 Bundle — 自带 patch 层的插件包，作为一层加入 profile
/// </summary>
public sealed class PluginBundle
{
    /// <summary>bundle 名</summary>
    public required string Name { get; init; }

    /// <summary>自带 patch 层</summary>
    public PluginPatch? Patch { get; init; }
}

/// <summary>
/// 插件 Profile — bundle 依赖声明 + 自身 patch
/// <para>对齐 DSH profile.bundles + profile 自身 cordis.patch.yml</para>
/// </summary>
public sealed class PluginProfile
{
    /// <summary>profile 名</summary>
    public required string Name { get; init; }

    /// <summary>bundle 依赖列表（按序）</summary>
    public List<PluginBundle> Bundles { get; init; } = new();

    /// <summary>profile 自身 patch 层</summary>
    public PluginPatch? Patch { get; init; }

    /// <summary>
    /// 收集所有 patch 层（bundle patch 按序 + profile patch）
    /// </summary>
    public IReadOnlyList<PluginPatch> CollectLayers()
    {
        var layers = new List<PluginPatch>();
        for (int i = 0; i < Bundles.Count; i++)
        {
            if (Bundles[i].Patch is { } bp) layers.Add(bp);
        }
        if (Patch is { } pp) layers.Add(pp);
        return layers;
    }
}
