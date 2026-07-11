namespace Core.Context;

/// <summary>
/// 选项工厂接口 — 创建 ChatOptions 执行设置
/// </summary>
public interface IChatOptionsFactory
{
    /// <summary>
    /// 创建 ChatOptions
    /// </summary>
    ChatOptions Create();
}
