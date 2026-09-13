namespace McpProtocol.Contracts;

public static class JsonRpc
{
    public const string ProtocolVersion = "2.0";
    public const string ContentLengthPrefix = "Content-Length: ";
}

public static class McpProtocolVersion
{
    public const string V2024_11_05 = "2024-11-05";
    public const string V2025_03_26 = "2025-03-26";
    public const string V2025_06_18 = "2025-06-18";
    public const string V2025_11_25 = "2025-11-25";

    public const string Current = V2025_11_25;

    public static readonly FrozenSet<string> Supported = FrozenSet.Create(
        StringComparer.Ordinal,
        V2025_11_25, V2025_06_18, V2025_03_26);
}

public static class ErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

public static class JsonValueTypes
{
    public const string String = "string";
    public const string Integer = "integer";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Object = "object";
    public const string Array = "array";
}
