namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 节点生命周期状态枚举
/// </summary>
public enum NodeState {
    /// <summary>已定义但尚未加载</summary>
    Defined,
    /// <summary>正在加载中</summary>
    Loading,
    /// <summary>已激活，可正常使用</summary>
    Active,
    /// <summary>正在卸载中</summary>
    Unloading,
    /// <summary>已卸载，不可使用</summary>
    Unloaded,
    /// <summary>加载或运行失败</summary>
    Failed
}
