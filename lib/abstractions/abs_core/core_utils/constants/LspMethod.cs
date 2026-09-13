namespace JoinCode.Abstractions.Utils;

/// <summary>
/// LSP 协议方法名枚举 — 替代原 LspMethods 静态常量类
/// </summary>
public enum LspMethod
{
    [EnumValue("initialize")] Initialize,
    [EnumValue("initialized")] Initialized,
    [EnumValue("shutdown")] Shutdown,
    [EnumValue("textDocument/didOpen")] TextDocumentDidOpen,
    [EnumValue("textDocument/definition")] TextDocumentDefinition,
    [EnumValue("textDocument/references")] TextDocumentReferences,
    [EnumValue("textDocument/hover")] TextDocumentHover,
    [EnumValue("textDocument/completion")] TextDocumentCompletion,
    [EnumValue("textDocument/documentSymbol")] TextDocumentDocumentSymbol,
    [EnumValue("workspace/symbol")] WorkspaceSymbol,
    [EnumValue("textDocument/implementation")] TextDocumentImplementation,
    [EnumValue("textDocument/prepareCallHierarchy")] TextDocumentPrepareCallHierarchy,
    [EnumValue("callHierarchy/incomingCalls")] CallHierarchyIncomingCalls,
    [EnumValue("callHierarchy/outgoingCalls")] CallHierarchyOutgoingCalls,
}
