namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class VimStateHandlerAttribute : Attribute {
    /// <summary>获取状态类型名称。</summary>
    public string StateTypeName { get; }

    /// <summary>构造 VimStateHandlerAttribute 实例。</summary>
    public VimStateHandlerAttribute(string stateTypeName) {
        StateTypeName = stateTypeName ?? throw new ArgumentNullException(nameof(stateTypeName));
    }
}
