namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// Bedrock 模型辅助工具 — 对齐 TS 原版 src/utils/model/bedrock.ts
/// <para>提供跨区域推理前缀(inference profile prefix)的提取与应用</para>
/// <para>Bedrock 跨区域前缀: us / eu / apac / global — IAM 权限可能限定特定区域</para>
/// <para>子代理继承父级前缀,避免区域不匹配导致权限错误</para>
/// </summary>
public static class BedrockModelHelper
{
    /// <summary>
    /// Bedrock 跨区域推理前缀列表 — 对齐 TS 原版 BEDROCK_REGION_PREFIXES
    /// <para>us: 美国区域, eu: 欧洲区域, apac: 亚太区域, global: 全球</para>
    /// </summary>
    public static readonly string[] RegionPrefixes = ["us", "eu", "apac", "global"];

    /// <summary>
    /// 判断模型 ID 是否是 Bedrock foundation model — 对齐 TS 原版 isFoundationModel
    /// <para>foundation model 以 "anthropic." 开头(如 "anthropic.claude-sonnet-4-5-20250929-v1:0")</para>
    /// </summary>
    public static bool IsFoundationModel(string modelId)
    {
        return !string.IsNullOrEmpty(modelId) && modelId.StartsWith("anthropic.", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 Bedrock ARN 提取模型/推理配置 ID — 对齐 TS 原版 extractModelIdFromArn
    /// <para>非 ARN 格式直接返回原值</para>
    /// <para>ARN 格式: arn:aws:bedrock:{region}:{account}:inference-profile/{profile-id}</para>
    /// <para>也处理: arn:aws:bedrock:{region}:{account}:application-inference-profile/{profile-id}</para>
    /// <para>以及 foundation model ARN: arn:aws:bedrock:{region}::foundation-model/{model-id}</para>
    /// </summary>
    public static string ExtractModelIdFromArn(string modelId)
    {
        if (string.IsNullOrEmpty(modelId) || !modelId.StartsWith("arn:", StringComparison.Ordinal))
            return modelId;

        var lastSlashIndex = modelId.LastIndexOf('/');
        if (lastSlashIndex < 0)
            return modelId;

        return modelId[(lastSlashIndex + 1)..];
    }

    /// <summary>
    /// 从 Bedrock 跨区域推理模型 ID 提取区域前缀 — 对齐 TS 原版 getBedrockRegionPrefix
    /// <para>处理纯模型 ID 和完整 ARN 格式</para>
    /// <para>示例:</para>
    /// <para>  "eu.anthropic.claude-sonnet-4-5-20250929-v1:0" → "eu"</para>
    /// <para>  "us.anthropic.claude-3-7-sonnet-20250219-v1:0" → "us"</para>
    /// <para>  "arn:aws:bedrock:ap-northeast-2:123:inference-profile/global.anthropic.claude-opus-4-6-v1" → "global"</para>
    /// <para>  "anthropic.claude-3-5-sonnet-20241022-v2:0" → null(foundation model,无前缀)</para>
    /// <para>  "claude-sonnet-4-5-20250929" → null(第一方格式,无前缀)</para>
    /// </summary>
    public static string? GetBedrockRegionPrefix(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return null;

        var effectiveModelId = ExtractModelIdFromArn(modelId);

        foreach (var prefix in RegionPrefixes)
        {
            if (effectiveModelId.StartsWith($"{prefix}.anthropic.", StringComparison.Ordinal))
                return prefix;
        }
        return null;
    }

    /// <summary>
    /// 给 Bedrock 模型 ID 应用区域前缀 — 对齐 TS 原版 applyBedrockRegionPrefix
    /// <para>若模型已有不同区域前缀,替换之</para>
    /// <para>若是 foundation model(anthropic.*),添加前缀</para>
    /// <para>若非 Bedrock 模型格式,原样返回</para>
    /// <para>示例:</para>
    /// <para>  Apply("us.anthropic.claude-sonnet-4-5-v1:0", "eu") → "eu.anthropic.claude-sonnet-4-5-v1:0"</para>
    /// <para>  Apply("anthropic.claude-sonnet-4-5-v1:0", "eu") → "eu.anthropic.claude-sonnet-4-5-v1:0"</para>
    /// <para>  Apply("claude-sonnet-4-5-20250929", "eu") → "claude-sonnet-4-5-20250929"(非 Bedrock 模型)</para>
    /// </summary>
    public static string ApplyBedrockRegionPrefix(string modelId, string prefix)
    {
        if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(prefix))
            return modelId;

        var existingPrefix = GetBedrockRegionPrefix(modelId);
        if (existingPrefix is not null)
            return modelId.Replace($"{existingPrefix}.", $"{prefix}.", StringComparison.Ordinal);

        if (IsFoundationModel(modelId))
            return $"{prefix}.{modelId}";

        return modelId;
    }

    /// <summary>
    /// 应用父级区域前缀到子代理模型 — 对齐 TS 原版 getAgentModel.applyParentRegionPrefix
    /// <para>若 originalSpec 已显式携带区域前缀,保留之(避免数据驻留违规)</para>
    /// <para>否则用父级前缀覆盖(确保子代理与父级同区域)</para>
    /// <para>非 Bedrock provider 或父级无前缀时原样返回</para>
    /// </summary>
    /// <param name="resolvedModel">解析后的模型(可能是 alias 或完整 ID)</param>
    /// <param name="originalSpec">原始模型规格(解析前)</param>
    /// <param name="parentRegionPrefix">父级区域前缀(从父模型提取)</param>
    /// <param name="isBedrockProvider">当前 provider 是否是 Bedrock</param>
    public static string ApplyParentRegionPrefix(
        string resolvedModel,
        string originalSpec,
        string? parentRegionPrefix,
        bool isBedrockProvider)
    {
        if (string.IsNullOrEmpty(parentRegionPrefix) || !isBedrockProvider)
            return resolvedModel;

        if (GetBedrockRegionPrefix(originalSpec) is not null)
            return resolvedModel;

        return ApplyBedrockRegionPrefix(resolvedModel, parentRegionPrefix);
    }
}
