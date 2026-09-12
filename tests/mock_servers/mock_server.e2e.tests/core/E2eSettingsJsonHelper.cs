namespace MockServer.E2E.Tests.Core;

/// <summary>
/// E2E 测试 settings.json 写入工具 — 让所有 E2E 测试共享同一份含完整 vendor 节点的 settings.json
/// <para>模型 ID 与 DualRoleConversationRunner 的 ModelId 映射保持一致</para>
/// </summary>
internal static class E2eSettingsJsonHelper
{
    /// <summary>
    /// 在 stateDir 写入含完整 vendor 节点的 settings.json — 让 ProviderDefinitionRegistry 能注册所有供应商
    /// <para>E2E 隔离的 AppData 目录无用户 settings.json，需测试 setup 提供，否则 registry 只有 azure</para>
    /// <para>模型 ID 必须与 DualRoleConversationRunner 的 ModelId 映射一致，否则 [GRD016] 报错</para>
    /// </summary>
    public static void WriteSettingsJsonToStateDir(string stateDir)
    {
        var settingsJson = """
        {
          "vendor": {
            "openai": {
              "protocol": "openai-compatible",
              "apiKeyEnvVar": "OPENAI_API_KEY",
              "models": [
                { "id": "gpt-4o", "displayName": "GPT-4o", "capabilities": { "modalities": ["text"] } },
                { "id": "gpt-4o-mini", "displayName": "GPT-4o Mini", "capabilities": { "modalities": ["text"] } }
              ]
            },
            "anthropic": {
              "protocol": "anthropic",
              "apiKeyEnvVar": "ANTHROPIC_API_KEY",
              "models": [
                { "id": "claude-sonnet-4-20250514", "displayName": "Claude Sonnet 4", "capabilities": { "modalities": ["text"] } }
              ]
            },
            "deepseek": {
              "protocol": "openai-compatible",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "models": [
                { "id": "deepseek-v4-flash", "displayName": "DeepSeek V4 Flash", "capabilities": { "modalities": ["text"] } }
              ]
            },
            "agnes": {
              "protocol": "openai-compatible",
              "apiKeyEnvVar": "AGNES_API_KEY",
              "models": [
                { "id": "agnes-image-2.0-flash", "displayName": "Agnes Image 2.0 Flash", "capabilities": { "modalities": ["text", "readImage"] } }
              ]
            },
            "sensenova": {
              "protocol": "openai-compatible",
              "apiKeyEnvVar": "SENSENOVA_API_KEY",
              "models": [
                { "id": "sensenova-6.8-flash-lite", "displayName": "SenseNova 6.8 Flash Lite", "capabilities": { "modalities": ["text"] } }
              ]
            }
          }
        }
        """;
        IO.FileSystem.SafeFileIO.WriteAllText(System.IO.Path.Combine(stateDir, "settings.json"), settingsJson);
    }
}
