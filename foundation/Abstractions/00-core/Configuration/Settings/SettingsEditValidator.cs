namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// settings.json 文件编辑校验器。
/// 对齐 TS: validateInputForSettingsFileEdit — 只阻止"从合法变非法"的降级编辑，不阻止修复。
/// </summary>
public static class SettingsEditValidator
{
    /// <summary>
    /// 判断文件路径是否为 JCC settings 文件。
    /// 对齐 TS: isClaudeSettingsPath — 识别 .jcc/settings.json 和 .jcc/settings.local.json
    /// </summary>
    public static bool IsJccSettingsPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalized = filePath.Replace('/', '\\');
        return normalized.EndsWith("\\.jcc\\settings.json", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("\\.jcc\\settings.local.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 校验 settings 文件编辑是否合法。
    /// 对齐 TS: validateInputForSettingsFileEdit
    /// 核心原则: 只阻止"从合法变非法"的降级编辑，不阻止修复。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="originalContent">编辑前的文件内容</param>
    /// <param name="updatedContent">编辑后的文件内容（由调用方模拟编辑得到）</param>
    /// <returns>null 表示允许编辑；非 null 表示拒绝编辑的错误消息</returns>
    public static string? ValidateEdit(string filePath, string originalContent, string updatedContent)
    {
        if (!IsJccSettingsPath(filePath))
            return null;

        // 编辑前内容无效 → 允许编辑（鼓励修复已损坏的 settings）
        var beforeResult = ValidateSettingsContent(originalContent);
        if (!beforeResult.IsValid)
            return null;

        // 编辑前有效 + 编辑后无效 → 拒绝
        var afterResult = ValidateSettingsContent(updatedContent);
        if (!afterResult.IsValid)
        {
            return $"JCC settings.json 验证失败:\n{afterResult.Error}\n\n注意: 除非明确指示，否则不要更新 env 字段。";
        }

        return null;
    }

    /// <summary>
    /// 验证 settings.json 内容是否合法。
    /// 对齐 TS: validateSettingsFileContent — 使用 JsonDocument 做基础结构验证。
    /// </summary>
    internal static SettingsValidationResult ValidateSettingsContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return SettingsValidationResult.Invalid("内容为空");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            return SettingsValidationResult.Invalid($"无效的 JSON: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return SettingsValidationResult.Invalid("根元素必须是 JSON 对象");

            var errors = new List<string>();
            var root = doc.RootElement;

            // 验证已知字段的类型
            ValidateOptionalString(root, "model", errors);
            ValidateOptionalString(root, "apiKeyHelper", errors);
            ValidateOptionalString(root, "awsCredentialExport", errors);
            ValidateOptionalString(root, "defaultShell", errors);
            ValidateOptionalString(root, "effortLevel", errors);

            ValidateOptionalObject(root, "env", errors);
            ValidateOptionalObject(root, "permissions", errors);
            ValidateOptionalObject(root, "hooks", errors);
            ValidateOptionalObject(root, "mcpServers", errors);
            ValidateOptionalObject(root, "sandbox", errors);
            ValidateOptionalObject(root, "enabledPlugins", errors);

            ValidateOptionalArray(root, "availableModels", errors);
            ValidateOptionalArray(root, "allowedMcpServers", errors);
            ValidateOptionalArray(root, "deniedMcpServers", errors);

            ValidateOptionalNumber(root, "cleanupPeriodDays", errors);

            ValidateOptionalBoolean(root, "strictPluginOnlyCustomization", errors);

            // env 字段的值必须是字符串
            if (root.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in env.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        errors.Add($"env.{prop.Name}: 值必须是字符串");
                }
            }

            // permissions.defaultMode 必须是已知值
            if (root.TryGetProperty("permissions", out var perms)
                && perms.ValueKind == JsonValueKind.Object
                && perms.TryGetProperty("defaultMode", out var mode)
                && mode.ValueKind == JsonValueKind.String)
            {
                var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "default", "acceptEdits", "plan", "bypassPermissions"
                };
                if (!validModes.Contains(mode.GetString()!))
                    errors.Add($"permissions.defaultMode: 无效的模式 '{mode.GetString()}'，有效值: default, acceptEdits, plan, bypassPermissions");
            }

            if (errors.Count > 0)
                return SettingsValidationResult.Invalid(string.Join("\n", errors));
        }

        return SettingsValidationResult.Valid();
    }

    private static void ValidateOptionalString(JsonElement root, string key, List<string> errors)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.String)
            errors.Add($"{key}: 必须是字符串");
    }

    private static void ValidateOptionalObject(JsonElement root, string key, List<string> errors)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Object)
            errors.Add($"{key}: 必须是 JSON 对象");
    }

    private static void ValidateOptionalArray(JsonElement root, string key, List<string> errors)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Array)
            errors.Add($"{key}: 必须是 JSON 数组");
    }

    private static void ValidateOptionalNumber(JsonElement root, string key, List<string> errors)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Number)
            errors.Add($"{key}: 必须是数字");
    }

    private static void ValidateOptionalBoolean(JsonElement root, string key, List<string> errors)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False)
            errors.Add($"{key}: 必须是布尔值");
    }
}

/// <summary>
/// settings.json 验证结果
/// </summary>
internal sealed record SettingsValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }

    public static SettingsValidationResult Valid() => new() { IsValid = true };
    public static SettingsValidationResult Invalid(string error) => new() { IsValid = false, Error = error };
}
