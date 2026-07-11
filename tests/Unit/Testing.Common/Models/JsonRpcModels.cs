namespace Testing.Common.Models;

/// <summary>
/// JSON-RPC请求模型
/// </summary>
public class JsonRpcRequest
{
    public string JsonRpc { get; set; } = "2.0";
    public JsonElement Id { get; set; }
    public string Method { get; set; } = "";
    public Dictionary<string, JsonElement>? Params { get; set; }
}
