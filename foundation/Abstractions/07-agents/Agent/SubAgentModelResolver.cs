namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理模型解析器 — 对齐 TS 原版 src/utils/model/agent.ts
/// <para>提供 inherit 关键字判断、alias 匹配父 tier、显示文本等纯函数</para>
/// <para>放在 Abstractions 层供 Brain 和 Agents 共用,避免循环依赖</para>
/// </summary>
public static class SubAgentModelResolver
{
    /// <summary>
    /// 子代理默认模型关键字 — 对齐 TS 原版 getDefaultSubagentModel
    /// <para>返回 "inherit" 表示子代理默认继承父线程模型</para>
    /// </summary>
    public const string DefaultSubagentModel = "inherit";

    /// <summary>
    /// 判断模型字符串是否是 inherit 关键字 — 对齐 TS 原版 agentModelWithExp === 'inherit'
    /// <para>不区分大小写: "inherit"、"Inherit"、"INHERIT" 均返回 true</para>
    /// <para>null/空白 返回 false</para>
    /// </summary>
    public static bool IsInheritKeyword(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;
        return string.Equals(model, DefaultSubagentModel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断 agent 指定的 model alias 是否匹配父模型 tier
    /// <para>对齐 TS 原版 aliasMatchesParentTier — 避免 Vertex 用户从 Opus 4.6 降级到默认 Opus</para>
    /// <para>alias = "opus" 且 parentModel 含 "opus" → true(用父模型,避免降级)</para>
    /// <para>仅裸 family alias 匹配,opus[1m]/best/opusplan 不匹配(它们携带额外语义)</para>
    /// </summary>
    public static bool AliasMatchesParentTier(string? alias, string parentModel)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(parentModel))
            return false;

        var aliasLower = alias.ToLowerInvariant();
        var parentLower = parentModel.ToLowerInvariant();

        return aliasLower switch
        {
            "opus" => parentLower.Contains("opus"),
            "sonnet" => parentLower.Contains("sonnet"),
            "haiku" => parentLower.Contains("haiku"),
            _ => false,
        };
    }

    /// <summary>
    /// 获取子代理模型显示文本 — 对齐 TS 原版 getAgentModelDisplay
    /// <para>null/空 → "Inherit from parent (default)"</para>
    /// <para>"inherit" → "Inherit from parent"</para>
    /// <para>其他 → 首字母大写</para>
    /// </summary>
    public static string GetAgentModelDisplay(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return "Inherit from parent (default)";
        if (IsInheritKeyword(model))
            return "Inherit from parent";
        if (model.Length == 1)
            return char.ToUpperInvariant(model[0]).ToString();
        return char.ToUpperInvariant(model[0]) + model[1..];
    }

    /// <summary>
    /// 解析子代理最终生效模型(不含环境变量覆盖) — 对齐 TS 原版 getAgentModel 主体逻辑
    /// <para>优先级: spawnModel > definitionModel > inherit/父级模型</para>
    /// <para>"inherit" 关键字或 null → 返回 parentModel(继承父级)</para>
    /// <para>alias 匹配父 tier → 返回 parentModel(避免降级)</para>
    /// </summary>
    /// <param name="spawnModel">调用时覆盖(AgentSpawnOptions.Model)</param>
    /// <param name="definitionModel">定义文件模型(AgentDefinition.ModelName)</param>
    /// <param name="parentModel">父线程模型(主代理模型)</param>
    /// <returns>最终生效模型</returns>
    public static string? ResolveModel(string? spawnModel, string? definitionModel, string? parentModel)
    {
        var selected = spawnModel ?? definitionModel;

        if (selected is null || IsInheritKeyword(selected))
            return parentModel;

        if (parentModel is not null && AliasMatchesParentTier(selected, parentModel))
            return parentModel;

        return selected;
    }

    /// <summary>
    /// 解析子代理最终生效模型(含 Bedrock 跨区域前缀继承) — 对齐 TS 原版 getAgentModel 完整逻辑
    /// <para>优先级链同 ResolveModel,额外在解析后应用 Bedrock 区域前缀</para>
    /// <para>Bedrock 前缀继承: 若父模型有区域前缀且 provider 是 Bedrock,子代理模型也应用相同前缀</para>
    /// <para>例外: alias 匹配父 tier → 返回父模型(不应用前缀);子代理已显式指定前缀 → 保留</para>
    /// </summary>
    /// <param name="spawnModel">调用时覆盖(AgentSpawnOptions.Model)</param>
    /// <param name="definitionModel">定义文件模型(AgentDefinition.ModelName)</param>
    /// <param name="parentModel">父线程模型(主代理模型)</param>
    /// <param name="parentRegionPrefix">父级 Bedrock 区域前缀(从父模型提取,null 表示无前缀)</param>
    /// <param name="isBedrockProvider">当前 provider 是否是 Bedrock</param>
    /// <returns>最终生效模型(含 Bedrock 区域前缀)</returns>
    public static string? ResolveModelWithBedrock(
        string? spawnModel,
        string? definitionModel,
        string? parentModel,
        string? parentRegionPrefix,
        bool isBedrockProvider)
    {
        var selected = spawnModel ?? definitionModel;

        if (selected is null || IsInheritKeyword(selected))
            return parentModel;

        if (parentModel is not null && AliasMatchesParentTier(selected, parentModel))
            return parentModel;

        if (string.IsNullOrEmpty(selected))
            return selected;

        return BedrockModelHelper.ApplyParentRegionPrefix(
            selected, selected, parentRegionPrefix, isBedrockProvider);
    }
}
