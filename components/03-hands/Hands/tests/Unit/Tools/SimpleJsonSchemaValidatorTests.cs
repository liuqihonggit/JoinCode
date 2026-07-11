namespace Core.Tests.Tools;

public sealed class SimpleJsonSchemaValidatorTests
{
    private readonly SimpleJsonSchemaValidator _validator = new();

    #region Type Validation

    [Fact]
    public void Validate_ValidStringType_ShouldPass()
    {
        var schema = """{"type": "string"}""";
        var result = _validator.Validate("\"hello\"", schema);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_InvalidStringType_ShouldFail()
    {
        var schema = """{"type": "string"}""";
        var result = _validator.Validate("42", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$");
    }

    [Fact]
    public void Validate_ValidNumberType_ShouldPass()
    {
        var schema = """{"type": "number"}""";
        var result = _validator.Validate("3.14", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_IntegerAsNumberType_ShouldPass()
    {
        var schema = """{"type": "number"}""";
        var result = _validator.Validate("42", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidIntegerType_ShouldPass()
    {
        var schema = """{"type": "integer"}""";
        var result = _validator.Validate("42", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_FloatAsIntegerType_ShouldFail()
    {
        var schema = """{"type": "integer"}""";
        var result = _validator.Validate("3.14", schema);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ValidBooleanType_ShouldPass()
    {
        var schema = """{"type": "boolean"}""";
        var result = _validator.Validate("true", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidArrayType_ShouldPass()
    {
        var schema = """{"type": "array"}""";
        var result = _validator.Validate("[1, 2, 3]", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidObjectType_ShouldPass()
    {
        var schema = """{"type": "object"}""";
        var result = _validator.Validate("""{"key": "value"}""", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullType_ShouldPass()
    {
        var schema = """{"type": "null"}""";
        var result = _validator.Validate("null", schema);
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Required Properties

    [Fact]
    public void Validate_AllRequiredPropertiesPresent_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                }
            }
            """;
        var instance = """{"name": "Alice", "age": 30}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingRequiredProperty_ShouldFail()
    {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                }
            }
            """;
        var instance = """{"name": "Alice"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$.age" && e.Message.Contains("age"));
    }

    [Fact]
    public void Validate_MissingMultipleRequiredProperties_ShouldReportAllErrors()
    {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age", "email"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"},
                    "email": {"type": "string"}
                }
            }
            """;
        var instance = """{"name": "Alice"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    #endregion

    #region String Constraints

    [Fact]
    public void Validate_StringMinLengthSatisfied_ShouldPass()
    {
        var schema = """{"type": "string", "minLength": 3}""";
        var result = _validator.Validate("\"hello\"", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_StringMinLengthViolated_ShouldFail()
    {
        var schema = """{"type": "string", "minLength": 5}""";
        var result = _validator.Validate("\"hi\"", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最小长度"));
    }

    [Fact]
    public void Validate_StringMaxLengthSatisfied_ShouldPass()
    {
        var schema = """{"type": "string", "maxLength": 10}""";
        var result = _validator.Validate("\"hello\"", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_StringMaxLengthViolated_ShouldFail()
    {
        var schema = """{"type": "string", "maxLength": 3}""";
        var result = _validator.Validate("\"hello\"", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最大长度"));
    }

    #endregion

    #region Number Constraints

    [Fact]
    public void Validate_NumberMinimumSatisfied_ShouldPass()
    {
        var schema = """{"type": "integer", "minimum": 0}""";
        var result = _validator.Validate("5", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NumberMinimumViolated_ShouldFail()
    {
        var schema = """{"type": "integer", "minimum": 10}""";
        var result = _validator.Validate("5", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最小值"));
    }

    [Fact]
    public void Validate_NumberMaximumSatisfied_ShouldPass()
    {
        var schema = """{"type": "integer", "maximum": 100}""";
        var result = _validator.Validate("50", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NumberMaximumViolated_ShouldFail()
    {
        var schema = """{"type": "integer", "maximum": 10}""";
        var result = _validator.Validate("50", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最大值"));
    }

    #endregion

    #region Array Constraints

    [Fact]
    public void Validate_ArrayMinItemsSatisfied_ShouldPass()
    {
        var schema = """{"type": "array", "minItems": 2}""";
        var result = _validator.Validate("[1, 2, 3]", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArrayMinItemsViolated_ShouldFail()
    {
        var schema = """{"type": "array", "minItems": 5}""";
        var result = _validator.Validate("[1, 2, 3]", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最少需要"));
    }

    [Fact]
    public void Validate_ArrayMaxItemsSatisfied_ShouldPass()
    {
        var schema = """{"type": "array", "maxItems": 5}""";
        var result = _validator.Validate("[1, 2, 3]", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArrayMaxItemsViolated_ShouldFail()
    {
        var schema = """{"type": "array", "maxItems": 2}""";
        var result = _validator.Validate("[1, 2, 3]", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$" && e.Message.Contains("最多允许"));
    }

    [Fact]
    public void Validate_ArrayItemsSchema_ShouldValidateEachItem()
    {
        var schema = """{"type": "array", "items": {"type": "string"}}""";
        var result = _validator.Validate("""["a", "b", "c"]""", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ArrayItemsSchemaViolation_ShouldFail()
    {
        var schema = """{"type": "array", "items": {"type": "string"}}""";
        var result = _validator.Validate("""["a", 42, "c"]""", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "$[1]");
    }

    #endregion

    #region Nested Object Validation

    [Fact]
    public void Validate_NestedObjectValid_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "required": ["user"],
                "properties": {
                    "user": {
                        "type": "object",
                        "required": ["name"],
                        "properties": {
                            "name": {"type": "string"},
                            "age": {"type": "integer"}
                        }
                    }
                }
            }
            """;
        var instance = """{"user": {"name": "Alice", "age": 30}}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NestedObjectMissingRequired_ShouldFail()
    {
        var schema = """
            {
                "type": "object",
                "required": ["user"],
                "properties": {
                    "user": {
                        "type": "object",
                        "required": ["name"],
                        "properties": {
                            "name": {"type": "string"}
                        }
                    }
                }
            }
            """;
        var instance = """{"user": {"age": 30}}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$.user.name");
    }

    [Fact]
    public void Validate_NestedObjectWrongType_ShouldFail()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "user": {
                        "type": "object",
                        "properties": {
                            "name": {"type": "string"}
                        }
                    }
                }
            }
            """;
        var instance = """{"user": "not an object"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$.user");
    }

    #endregion

    #region Enum Validation

    [Fact]
    public void Validate_EnumValueInList_ShouldPass()
    {
        var schema = """{"type": "string", "enum": ["red", "green", "blue"]}""";
        var result = _validator.Validate("\"red\"", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EnumValueNotInList_ShouldFail()
    {
        var schema = """{"type": "string", "enum": ["red", "green", "blue"]}""";
        var result = _validator.Validate("\"yellow\"", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("枚举"));
    }

    [Fact]
    public void Validate_EnumWithMixedTypes_ShouldPass()
    {
        var schema = """{"enum": ["active", 1, true, null]}""";
        var result = _validator.Validate("1", schema);
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Additional Properties (Strict Mode)

    [Fact]
    public void Validate_AdditionalPropertiesAllowed_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"}
                },
                "additionalProperties": true
            }
            """;
        var instance = """{"name": "Alice", "extra": "field"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AdditionalPropertiesForbidden_ShouldFail()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"}
                },
                "additionalProperties": false
            }
            """;
        var instance = """{"name": "Alice", "extra": "field"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$.extra" && e.Message.Contains("额外属性"));
    }

    [Fact]
    public void Validate_NoAdditionalProperties_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"}
                },
                "additionalProperties": false
            }
            """;
        var instance = """{"name": "Alice"}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Invalid Input

    [Fact]
    public void Validate_InvalidJsonInstance_ShouldReturnError()
    {
        var schema = """{"type": "string"}""";
        var result = _validator.Validate("not valid json{", schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("无效的JSON实例"));
    }

    [Fact]
    public void Validate_InvalidSchemaJson_ShouldReturnError()
    {
        var result = _validator.Validate("\"hello\"", "not valid schema{");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("无效的JSON Schema"));
    }

    [Fact]
    public void Validate_NullInstance_ShouldThrowArgumentNullException()
    {
        var act = () => _validator.Validate(null!, """{"type": "string"}""");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullSchema_ShouldThrowArgumentNullException()
    {
        var act = () => _validator.Validate("\"hello\"", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Complex Schema

    [Fact]
    public void Validate_ComplexSchemaValidInstance_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "required": ["name", "tags"],
                "properties": {
                    "name": {
                        "type": "string",
                        "minLength": 1,
                        "maxLength": 100
                    },
                    "tags": {
                        "type": "array",
                        "minItems": 1,
                        "maxItems": 10,
                        "items": {
                            "type": "string"
                        }
                    },
                    "score": {
                        "type": "number",
                        "minimum": 0,
                        "maximum": 100
                    }
                },
                "additionalProperties": false
            }
            """;
        var instance = """{"name": "Test", "tags": ["important"], "score": 85.5}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ComplexSchemaMultipleViolations_ShouldReportAllErrors()
    {
        var schema = """
            {
                "type": "object",
                "required": ["name", "tags"],
                "properties": {
                    "name": {
                        "type": "string",
                        "minLength": 5
                    },
                    "tags": {
                        "type": "array",
                        "minItems": 1,
                        "items": {
                            "type": "string"
                        }
                    }
                }
            }
            """;
        var instance = """{"name": "Hi", "tags": [42]}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Validate_DeeplyNestedObject_ShouldPass()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "level1": {
                        "type": "object",
                        "properties": {
                            "level2": {
                                "type": "object",
                                "properties": {
                                    "value": {"type": "string"}
                                }
                            }
                        }
                    }
                }
            }
            """;
        var instance = """{"level1": {"level2": {"value": "deep"}}}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DeeplyNestedObjectViolation_ShouldFail()
    {
        var schema = """
            {
                "type": "object",
                "properties": {
                    "level1": {
                        "type": "object",
                        "properties": {
                            "level2": {
                                "type": "object",
                                "properties": {
                                    "value": {"type": "string"}
                                }
                            }
                        }
                    }
                }
            }
            """;
        var instance = """{"level1": {"level2": {"value": 123}}}""";
        var result = _validator.Validate(instance, schema);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$.level1.level2.value");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Validate_EmptyObjectWithNoRequired_ShouldPass()
    {
        var schema = """{"type": "object"}""";
        var result = _validator.Validate("{}", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyArrayWithNoConstraints_ShouldPass()
    {
        var schema = """{"type": "array"}""";
        var result = _validator.Validate("[]", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyStringWithMinLength_ShouldFail()
    {
        var schema = """{"type": "string", "minLength": 1}""";
        var result = _validator.Validate("\"\"", schema);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SchemaWithNoType_ShouldNotError()
    {
        var schema = """{"minLength": 1}""";
        var result = _validator.Validate("\"hello\"", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BooleanFalseValue_ShouldBeValidBoolean()
    {
        var schema = """{"type": "boolean"}""";
        var result = _validator.Validate("false", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroInteger_ShouldBeValid()
    {
        var schema = """{"type": "integer", "minimum": 0}""";
        var result = _validator.Validate("0", schema);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeNumber_ShouldBeValid()
    {
        var schema = """{"type": "number"}""";
        var result = _validator.Validate("-3.14", schema);
        result.IsValid.Should().BeTrue();
    }

    #endregion
}
