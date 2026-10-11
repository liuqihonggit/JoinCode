
namespace JoinCode.ChatCommands;

/// <summary>
/// 模型名称助手 — 将完整模型名称转换为规范名称,委托给 IModelConfigLoader 实现
/// </summary>
public sealed class ModelNameHelper(IModelConfigLoader? modelConfigLoader = null) {
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;

    /// <summary>
    /// 获取模型的规范名称,若配置加载器不可用则原样返回
    /// </summary>
    /// <param name="fullModelName">完整模型名称</param>
    /// <returns>规范名称;若加载器不可用则返回原始名称</returns>
    public string GetCanonicalName(string fullModelName) {
        return _modelConfigLoader?.GetCanonicalName(fullModelName) ?? fullModelName;
    }

    internal string FirstPartyNameToCanonical(string fullModelName) {
        return _modelConfigLoader?.GetCanonicalName(fullModelName) ?? fullModelName;
    }
}