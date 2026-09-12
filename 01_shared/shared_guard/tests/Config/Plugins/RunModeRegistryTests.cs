namespace Core.Tests.Plugins;

public sealed class RunModeRegistryTests
{
    [Fact]
    public void Get_Standard_HasFullToolset()
    {
        var desc = RunModeRegistry.Get(RunMode.Standard);
        Assert.Equal("Standard", desc.DisplayName);
        Assert.True(desc.SupportsSubAgents);
        Assert.True(desc.SupportsWorkflows);
        Assert.False(desc.SupportsRuntimeInspection);
        Assert.Contains("file_edit", desc.AvailableTools);
        Assert.Contains("workflow", desc.AvailableTools);
    }

    [Fact]
    public void Get_Code_HasCodeSdk()
    {
        var desc = RunModeRegistry.Get(RunMode.Code);
        Assert.Equal("Code", desc.DisplayName);
        Assert.Contains("code_sdk", desc.AvailableTools);
        Assert.True(desc.SupportsSubAgents);
    }

    [Fact]
    public void Get_Minimal_OnlyTwoTools()
    {
        var desc = RunModeRegistry.Get(RunMode.Minimal);
        Assert.Equal("Minimal", desc.DisplayName);
        Assert.Equal(2, desc.AvailableTools.Length);
        Assert.Contains("shell", desc.AvailableTools);
        Assert.Contains("str_replace_editor", desc.AvailableTools);
        Assert.False(desc.SupportsSubAgents);
        Assert.False(desc.SupportsWorkflows);
    }

    [Fact]
    public void Get_Creator_SupportsInspectionAndExperiments()
    {
        var desc = RunModeRegistry.Get(RunMode.Creator);
        Assert.Equal("Creator", desc.DisplayName);
        Assert.True(desc.SupportsRuntimeInspection);
        Assert.True(desc.SupportsPluginExperiments);
        Assert.Contains("inspect", desc.AvailableTools);
        Assert.Contains("plugin_define", desc.AvailableTools);
    }

    [Fact]
    public void List_ReturnsAllFourModes()
    {
        var all = RunModeRegistry.List();
        Assert.Equal(4, all.Count);
    }

    [Fact]
    public void Exists_TrueForAllDefinedModes()
    {
        Assert.True(RunModeRegistry.Exists(RunMode.Standard));
        Assert.True(RunModeRegistry.Exists(RunMode.Code));
        Assert.True(RunModeRegistry.Exists(RunMode.Minimal));
        Assert.True(RunModeRegistry.Exists(RunMode.Creator));
    }

    [Fact]
    public void Get_EachMode_ReturnsCorrectDescriptor()
    {
        Assert.Equal(RunMode.Standard, RunModeRegistry.Get(RunMode.Standard).Mode);
        Assert.Equal(RunMode.Code, RunModeRegistry.Get(RunMode.Code).Mode);
        Assert.Equal(RunMode.Minimal, RunModeRegistry.Get(RunMode.Minimal).Mode);
        Assert.Equal(RunMode.Creator, RunModeRegistry.Get(RunMode.Creator).Mode);
    }

    [Fact]
    public void Standard_AndMinimal_ShareShellTool()
    {
        var standard = RunModeRegistry.Get(RunMode.Standard);
        var minimal = RunModeRegistry.Get(RunMode.Minimal);
        Assert.Contains("shell", standard.AvailableTools);
        Assert.Contains("shell", minimal.AvailableTools);
    }

    [Fact]
    public void Creator_IsSupersetOfStandard()
    {
        var creator = RunModeRegistry.Get(RunMode.Creator);
        var standard = RunModeRegistry.Get(RunMode.Standard);
        foreach (var tool in standard.AvailableTools)
        {
            Assert.Contains(tool, creator.AvailableTools);
        }
    }
}
