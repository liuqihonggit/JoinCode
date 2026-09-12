namespace Core.Tests.Plugins;

public sealed class PluginPatchTests
{
    private static PluginConfigRow Row(string id, string? name = null, object? config = null) =>
        new() { Id = id, Name = name, Config = config };

    [Fact]
    public void Apply_Insert_AddsRows()
    {
        var rows = new List<PluginConfigRow> { Row("group1") };
        var patch = new PluginPatch
        {
            Entries = new()
            {
                new() { Id = "group1", Insert = new() { new() { Id = "new1", Name = "plugin-new" } } },
            },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("new1", result.Rows[1].Id);
        Assert.Equal("plugin-new", result.Rows[1].Name);
    }

    [Fact]
    public void Apply_ById_OverwritesConfig()
    {
        var rows = new List<PluginConfigRow> { Row("r1", "old", "old-config") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Config = "new-config" } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Equal("new-config", result.Rows[0].Config);
    }

    [Fact]
    public void Apply_NameMismatch_WarnsAndSkips()
    {
        var rows = new List<PluginConfigRow> { Row("r1", "actual-name") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Name = "wrong-name", Config = "x" } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Single(result.Warnings);
        Assert.Contains("不符", result.Warnings[0]);
        Assert.Null(result.Rows[0].Config);
    }

    [Fact]
    public void Apply_IdNotFound_WarnsAndSkips()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "nonexistent", Config = "x" } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Single(result.Warnings);
        Assert.Contains("nonexistent", result.Warnings[0]);
    }

    [Fact]
    public void Apply_InsertIdNotFound_WarnsAndSkips()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var patch = new PluginPatch
        {
            Entries = new()
            {
                new() { Id = "nonexistent", Insert = new() { new() { Id = "x" } } },
            },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Single(result.Warnings);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void ApplyLayers_MultipleLayersInOrder()
    {
        var rows = new List<PluginConfigRow> { Row("r1", config: "base") };
        var layer1 = new PluginPatch { Entries = new() { new() { Id = "r1", Config = "layer1" } } };
        var layer2 = new PluginPatch { Entries = new() { new() { Id = "r1", Config = "layer2" } } };
        var result = PluginPatchApplicator.ApplyLayers(rows, layer1, layer2);
        Assert.Equal("layer2", result.Rows[0].Config);
    }

    [Fact]
    public void Apply_ConfigFullReplace_NotDeepMerge()
    {
        var rows = new List<PluginConfigRow> { Row("r1", config: new { A = 1, B = 2 }) };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Config = new { A = 99 } } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.NotNull(result.Rows[0].Config);
    }

    [Fact]
    public void Apply_DisabledFlag()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Disabled = true } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.True(result.Rows[0].Disabled);
    }

    [Fact]
    public void Apply_EmptyPatch_NoChange()
    {
        var rows = new List<PluginConfigRow> { Row("r1"), Row("r2") };
        var patch = new PluginPatch();
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Equal(2, result.Rows.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Apply_InjectOverride()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Inject = new[] { "tools", "sessions" } } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Equal(new[] { "tools", "sessions" }, result.Rows[0].Inject);
    }

    [Fact]
    public void Apply_GroupOverride()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var patch = new PluginPatch
        {
            Entries = new() { new() { Id = "r1", Group = "new-group" } },
        };
        var result = PluginPatchApplicator.Apply(rows, patch);
        Assert.Equal("new-group", result.Rows[0].Group);
    }

    [Fact]
    public void ApplyLayers_CollectsAllWarnings()
    {
        var rows = new List<PluginConfigRow> { Row("r1") };
        var l1 = new PluginPatch { Entries = new() { new() { Id = "missing1" } } };
        var l2 = new PluginPatch { Entries = new() { new() { Id = "missing2" } } };
        var result = PluginPatchApplicator.ApplyLayers(rows, l1, l2);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void PluginProfile_CollectLayers_BundlePatchesThenProfilePatch()
    {
        var profile = new PluginProfile
        {
            Name = "test",
            Bundles = new()
            {
                new() { Name = "b1", Patch = new PluginPatch { Entries = new() { new() { Id = "x1" } } } },
                new() { Name = "b2", Patch = new PluginPatch { Entries = new() { new() { Id = "x2" } } } },
            },
            Patch = new PluginPatch { Entries = new() { new() { Id = "x3" } } },
        };
        var layers = profile.CollectLayers();
        Assert.Equal(3, layers.Count);
        Assert.Equal("x1", layers[0].Entries[0].Id);
        Assert.Equal("x2", layers[1].Entries[0].Id);
        Assert.Equal("x3", layers[2].Entries[0].Id);
    }

    [Fact]
    public void PluginProfile_CollectLayers_NoPatches_ReturnsEmpty()
    {
        var profile = new PluginProfile { Name = "empty" };
        var layers = profile.CollectLayers();
        Assert.Empty(layers);
    }
}
