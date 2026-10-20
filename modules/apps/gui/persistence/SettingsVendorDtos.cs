namespace JoinCode.Gui.Persistence;

/// <summary>settings.json vendor 根 DTO — vendor 为供应商名→条目字典（key 动态，不做成固定属性）</summary>
public sealed class SettingsVendorRootDto {
    /// <summary>供应商名→供应商条目（key 为供应商 id，动态）</summary>
    [JsonPropertyName("vendor")]
    public Dictionary<string, SettingsVendorEntryDto?> Vendor { get; set; } = [];
}

/// <summary>供应商条目 DTO — 含模型列表</summary>
public sealed class SettingsVendorEntryDto {
    /// <summary>模型列表</summary>
    [JsonPropertyName("models")]
    public List<SettingsModelIdDto?> Models { get; set; } = [];
}

/// <summary>模型 id DTO</summary>
public sealed class SettingsModelIdDto {
    /// <summary>模型 id</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
