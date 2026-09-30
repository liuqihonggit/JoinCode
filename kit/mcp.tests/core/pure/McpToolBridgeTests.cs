namespace Mcp.Tests;

/// <summary>
/// McpToolBridge 单元测试 — 验证 BuildParameters(Required 集合 + Enum 描述拼接) + MapSchemaTypeToClrType(6路 switch)
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class McpToolBridgeTests {
    [Theory]
    [InlineData("string", typeof(string))]
    [InlineData("integer", typeof(int))]
    [InlineData("number", typeof(double))]
    [InlineData("boolean", typeof(bool))]
    [InlineData("array", typeof(string))]
    [InlineData("object", typeof(string))]
    [InlineData("unknown", typeof(string))]
    [InlineData("", typeof(string))]
    public void MapSchemaTypeToClrType_ReturnsExpectedClrType(string schemaType, Type expected) {
        McpToolBridge.MapSchemaTypeToClrType(schemaType).Should().Be(expected);
    }

    [Fact]
    public void BuildParameters_EmptyProperties_ReturnsEmpty() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema()
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildParameters_SingleProperty_MapsNameTypeDescription() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["arg1"] = new ToolSchemaProperty { Type = "string", Description = "first arg" }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("arg1");
        result[0].Description.Should().Be("first arg");
        result[0].ParameterType.Should().Be(typeof(string));
        result[0].IsRequired.Should().BeFalse();
    }

    [Fact]
    public void BuildParameters_RequiredProperty_IsRequiredTrue() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["required_arg"] = new ToolSchemaProperty { Type = "integer" },
                    ["optional_arg"] = new ToolSchemaProperty { Type = "string" }
                },
                Required = ["required_arg"]
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        var required = result.Single(p => p.Name == "required_arg");
        var optional = result.Single(p => p.Name == "optional_arg");
        required.IsRequired.Should().BeTrue();
        optional.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void BuildParameters_NullDescription_DefaultsToEmpty() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["arg"] = new ToolSchemaProperty { Type = "string", Description = null }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result[0].Description.Should().BeEmpty();
    }

    [Fact]
    public void BuildParameters_EnumWithDescription_AppendsAllowedValues() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["mode"] = new ToolSchemaProperty {
                        Type = "string",
                        Description = "run mode",
                        Enum = ["fast", "slow"]
                    }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result[0].Description.Should().Be("run mode Allowed values: fast, slow");
    }

    [Fact]
    public void BuildParameters_EnumWithoutDescription_UsesAllowedValuesOnly() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["mode"] = new ToolSchemaProperty {
                        Type = "string",
                        Description = null,
                        Enum = ["a", "b", "c"]
                    }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result[0].Description.Should().Be("Allowed values: a, b, c");
    }

    [Fact]
    public void BuildParameters_EmptyEnum_NoAllowedValuesAppended() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["arg"] = new ToolSchemaProperty {
                        Type = "string",
                        Description = "desc",
                        Enum = []
                    }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result[0].Description.Should().Be("desc");
    }

    [Fact]
    public void BuildParameters_MultipleTypes_MappedCorrectly() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["s"] = new ToolSchemaProperty { Type = "string" },
                    ["i"] = new ToolSchemaProperty { Type = "integer" },
                    ["n"] = new ToolSchemaProperty { Type = "number" },
                    ["b"] = new ToolSchemaProperty { Type = "boolean" },
                    ["a"] = new ToolSchemaProperty { Type = "array" },
                    ["o"] = new ToolSchemaProperty { Type = "object" }
                }
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result.Single(p => p.Name == "s").ParameterType.Should().Be(typeof(string));
        result.Single(p => p.Name == "i").ParameterType.Should().Be(typeof(int));
        result.Single(p => p.Name == "n").ParameterType.Should().Be(typeof(double));
        result.Single(p => p.Name == "b").ParameterType.Should().Be(typeof(bool));
        result.Single(p => p.Name == "a").ParameterType.Should().Be(typeof(string));
        result.Single(p => p.Name == "o").ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void BuildParameters_RequiredListNull_DefaultsToEmpty() {
        var toolInfo = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["arg"] = new ToolSchemaProperty { Type = "string" }
                },
                Required = null!
            }
        };
        var result = McpToolBridge.BuildParameters(toolInfo);
        result[0].IsRequired.Should().BeFalse();
    }
}
