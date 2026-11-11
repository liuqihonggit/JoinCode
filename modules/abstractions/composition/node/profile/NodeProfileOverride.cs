namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// Profile 覆写条目 — 子 Profile 对父 Profile 中特定节点的属性覆写
/// </summary>
public sealed record NodeProfileOverride(
    string Consumer,
    string Node,
    int? Priority,
    FrozenDictionary<string, string>? Config
);
