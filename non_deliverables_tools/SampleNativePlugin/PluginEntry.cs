using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SampleNativePlugin;

/// <summary>
/// 示例 native 插件 — echo 方法 (ADR 0099)
/// <para>导出 plugin_load/plugin_invoke/plugin_unload C ABI 入口</para>
/// <para>用 NativeAOT 编译为 native DLL,宿主通过 NativeLibrary.Load 加载</para>
/// </summary>
public static class PluginEntry
{
    private static bool s_isLoaded;

    [UnmanagedCallersOnly(EntryPoint = "plugin_load")]
    public static unsafe int PluginLoad(byte* configPtr, int configLen)
    {
        s_isLoaded = true;
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "plugin_invoke")]
    public static unsafe int PluginInvoke(byte* reqPtr, int reqLen, byte* respPtr, int respCap)
    {
        if (!s_isLoaded) return (int)NativePluginError.NotLoaded;
        if (reqPtr == null || reqLen <= 0) return (int)NativePluginError.BadRequest;

        var requestJson = Encoding.UTF8.GetString(reqPtr, reqLen);
        var responseJson = HandleRequest(requestJson);
        var respBytes = Encoding.UTF8.GetBytes(responseJson);

        if (respBytes.Length > respCap)
            return (int)NativePluginError.BufferTooSmall;

        for (int i = 0; i < respBytes.Length; i++)
            respPtr[i] = respBytes[i];

        return respBytes.Length;
    }

    [UnmanagedCallersOnly(EntryPoint = "plugin_unload")]
    public static int PluginUnload()
    {
        s_isLoaded = false;
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "plugin_info")]
    public static unsafe int PluginInfo(byte* respPtr, int respCap)
    {
        var infoJson = """{"name":"SampleNativePlugin","version":"1.0.0","methods":["echo","ping"]}""";
        var infoBytes = Encoding.UTF8.GetBytes(infoJson);

        if (infoBytes.Length > respCap)
            return (int)NativePluginError.BufferTooSmall;

        for (int i = 0; i < infoBytes.Length; i++)
            respPtr[i] = infoBytes[i];

        return infoBytes.Length;
    }

    private static string HandleRequest(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("method", out var methodEl))
                return """{"ok":false,"error":"missing method"}""";

            var method = methodEl.GetString();

            if (method == "echo")
            {
                if (root.TryGetProperty("args", out var argsEl) &&
                    argsEl.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString() ?? "";
                    return $$"""{"ok":true,"value":"{{EscapeJson(text)}}"}""";
                }
                return """{"ok":false,"error":"missing args.text"}""";
            }

            if (method == "ping")
            {
                return """{"ok":true,"value":"pong"}""";
            }

            return $$"""{"ok":false,"error":"method not found: {{method}}}""";
        }
        catch (Exception ex)
        {
            return $$"""{"ok":false,"error":"{{EscapeJson(ex.Message)}}"}""";
        }
    }

    private static string EscapeJson(string s)
    {
        if (s.Length == 0) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>Native 插件错误码 — 与宿主侧 NativePluginError 对齐</summary>
internal enum NativePluginError : int
{
    Ok = 0,
    Generic = -1,
    BufferTooSmall = -2,
    MethodNotFound = -3,
    BadRequest = -4,
    NotLoaded = -5,
    InternalError = -6,
}
