namespace Tools;

/// <summary>
/// 简单JSON Schema验证器 - AOT兼容，支持基础Schema验证
/// 支持的Schema关键字: type, required, properties, additionalProperties,
/// minLength, maxLength, minimum, maximum, minItems, maxItems, items, enum
/// </summary>
[Register]
public sealed class SimpleJsonSchemaValidator : IJsonSchemaValidator
{
    /// <summary>
    /// 验证JSON Schema本身是否合法 — 对齐 TS ajv.validateSchema()
    /// 检查Schema结构是否符合JSON Schema规范的基本要求
    /// </summary>
    public SchemaValidationResult ValidateSchema(string schemaJson)
    {
        ArgumentNullException.ThrowIfNull(schemaJson);

        JsonNode? schemaNode;
        try
        {
            schemaNode = JsonNode.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = [new ValidationError { Path = "$", Message = L.T(StringKey.SchemaInvalidJsonSchema, ex.Message) }]
            };
        }

        if (schemaNode is not JsonObject schemaObj)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = [new ValidationError { Path = "$", Message = "Schema must be a JSON object" }]
            };
        }

        var errors = new List<ValidationError>();
        ValidateSchemaStructure(schemaObj, "$", errors);

        return new SchemaValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// 验证Schema结构合法性 — 递归检查
    /// </summary>
    private void ValidateSchemaStructure(JsonObject schema, string path, List<ValidationError> errors)
    {
        // type 必须是合法值
        if (schema.TryGetPropertyValue("type", out var typeNode))
        {
            if (typeNode is JsonValue typeValue && typeValue.TryGetValue(out string? typeStr))
            {
                var validTypes = new HashSet<string> { "object", "array", "string", "number", "integer", "boolean", "null" };
                if (!validTypes.Contains(typeStr))
                {
                    errors.Add(new ValidationError { Path = path, Message = $"Invalid type value: '{typeStr}'" });
                }
            }
            else if (typeNode is JsonArray typeArray)
            {
                // type 可以是数组
                foreach (var t in typeArray)
                {
                    if (t is JsonValue tv && tv.TryGetValue(out string? ts))
                    {
                        var validTypes = new HashSet<string> { "object", "array", "string", "number", "integer", "boolean", "null" };
                        if (!validTypes.Contains(ts))
                        {
                            errors.Add(new ValidationError { Path = path, Message = $"Invalid type value in array: '{ts}'" });
                        }
                    }
                }
            }
            else
            {
                errors.Add(new ValidationError { Path = path, Message = "Schema 'type' must be a string or array of strings" });
            }
        }

        // properties 必须是对象
        if (schema.TryGetPropertyValue("properties", out var propsNode) && propsNode is not JsonObject)
        {
            errors.Add(new ValidationError { Path = $"{path}.properties", Message = "Schema 'properties' must be an object" });
        }

        // required 必须是字符串数组
        if (schema.TryGetPropertyValue("required", out var requiredNode))
        {
            if (requiredNode is not JsonArray reqArray)
            {
                errors.Add(new ValidationError { Path = $"{path}.required", Message = "Schema 'required' must be an array" });
            }
            else
            {
                for (int i = 0; i < reqArray.Count; i++)
                {
                    if (reqArray[i] is not JsonValue rv || !rv.TryGetValue(out string? _))
                    {
                        errors.Add(new ValidationError { Path = $"{path}.required[{i}]", Message = "Each 'required' item must be a string" });
                    }
                }
            }
        }

        // enum 必须是数组
        if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is not JsonArray)
        {
            errors.Add(new ValidationError { Path = $"{path}.enum", Message = "Schema 'enum' must be an array" });
        }

        // items 必须是对象或数组
        if (schema.TryGetPropertyValue("items", out var itemsNode) && itemsNode is not JsonObject and not JsonArray)
        {
            errors.Add(new ValidationError { Path = $"{path}.items", Message = "Schema 'items' must be an object or array" });
        }

        // additionalProperties 必须是布尔值或对象
        if (schema.TryGetPropertyValue("additionalProperties", out var addPropsNode)
            && addPropsNode is not JsonValue and not JsonObject)
        {
            errors.Add(new ValidationError { Path = $"{path}.additionalProperties", Message = "Schema 'additionalProperties' must be a boolean or object" });
        }

        // 递归验证 properties 中的子 Schema
        if (propsNode is JsonObject propsObj)
        {
            foreach (var (propName, propSchema) in propsObj)
            {
                if (propSchema is JsonObject propSchemaObj)
                {
                    ValidateSchemaStructure(propSchemaObj, $"{path}.properties.{propName}", errors);
                }
            }
        }

        // 递归验证 items 中的子 Schema
        if (schema.TryGetPropertyValue("items", out var itemsNode2) && itemsNode2 is JsonObject itemsObj)
        {
            ValidateSchemaStructure(itemsObj, $"{path}.items", errors);
        }

        // 递归验证 additionalProperties 中的子 Schema
        if (schema.TryGetPropertyValue("additionalProperties", out var addPropsNode2) && addPropsNode2 is JsonObject addPropsObj)
        {
            ValidateSchemaStructure(addPropsObj, $"{path}.additionalProperties", errors);
        }
    }

    /// <summary>
    /// 验证JSON实例是否符合Schema
    /// </summary>
    /// <param name="jsonInstance">要验证的JSON字符串</param>
    /// <param name="schemaJson">JSON Schema字符串</param>
    /// <returns>验证结果</returns>
    public SchemaValidationResult Validate(string jsonInstance, string schemaJson)
    {
        ArgumentNullException.ThrowIfNull(jsonInstance);
        ArgumentNullException.ThrowIfNull(schemaJson);

        JsonNode? instanceNode;
        JsonNode? schemaNode;

        try
        {
            instanceNode = JsonNode.Parse(jsonInstance);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = [new ValidationError { Path = "$", Message = L.T(StringKey.SchemaInvalidJsonInstance, ex.Message) }]
            };
        }

        try
        {
            schemaNode = JsonNode.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = [new ValidationError { Path = "$", Message = L.T(StringKey.SchemaInvalidJsonSchema, ex.Message) }]
            };
        }

        var errors = new List<ValidationError>();
        ValidateNode(instanceNode, schemaNode, "$", errors);

        return new SchemaValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private void ValidateNode(JsonNode? instance, JsonNode? schema, string path, List<ValidationError> errors)
    {
        if (schema is not JsonObject schemaObj) return;

        // Type validation
        if (schemaObj.TryGetPropertyValue("type", out var typeNode))
        {
            ValidateType(instance, typeNode!, path, errors);
        }

        // Enum validation
        if (schemaObj.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArray)
        {
            ValidateEnum(instance, enumArray, path, errors);
        }

        // Object-specific validations
        if (instance is JsonObject instanceObj)
        {
            ValidateObject(instanceObj, schemaObj, path, errors);
        }

        // String-specific validations
        ValidateStringConstraints(instance, schemaObj, path, errors);

        // Number-specific validations
        ValidateNumberConstraints(instance, schemaObj, path, errors);

        // Array-specific validations
        if (instance is JsonArray instanceArray)
        {
            ValidateArray(instanceArray, schemaObj, path, errors);
        }
    }

    private static void ValidateType(JsonNode? instance, JsonNode typeNode, string path, List<ValidationError> errors)
    {
        var expectedType = typeNode!.GetValue<string>();
        var actualType = GetJsonTypeName(instance);

        bool typeMatch = expectedType switch
        {
            "number" => actualType is "number" or "integer",
            _ => actualType == expectedType
        };

        if (!typeMatch)
        {
            errors.Add(new ValidationError
            {
                Path = path,
                Message = L.T(StringKey.SchemaTypeMismatch, expectedType, actualType)
            });
        }
    }

    private static void ValidateEnum(JsonNode? instance, JsonArray enumValues, string path, List<ValidationError> errors)
    {
        foreach (var enumValue in enumValues)
        {
            if (JsonNodeDeepEquals(instance, enumValue)) return;
        }

        errors.Add(new ValidationError
        {
            Path = path,
            Message = L.T(StringKey.SchemaEnumValueNotAllowed)
        });
    }

    private void ValidateObject(JsonObject instance, JsonObject schema, string path, List<ValidationError> errors)
    {
        // Required properties
        if (schema.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray requiredArray)
        {
            foreach (var req in requiredArray)
            {
                var propName = req!.GetValue<string>();
                if (!instance.ContainsKey(propName))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.{propName}",
                        Message = L.T(StringKey.SchemaRequiredPropertyMissing, propName)
                    });
                }
            }
        }

        // Property schemas
        if (schema.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
        {
            foreach (var (propName, propSchema) in propsObj)
            {
                if (instance.TryGetPropertyValue(propName, out var propValue))
                {
                    ValidateNode(propValue, propSchema, $"{path}.{propName}", errors);
                }
            }
        }

        // Additional properties (strict mode)
        if (schema.TryGetPropertyValue("additionalProperties", out var additionalPropsNode))
        {
            if (additionalPropsNode is JsonValue additionalPropsValue
                && additionalPropsValue.TryGetValue(out bool allowAdditional)
                && !allowAdditional)
            {
                var definedProps = schema.TryGetPropertyValue("properties", out var dp) && dp is JsonObject dpObj
                    ? dpObj.Select(p => p.Key).ToHashSet()
                    : new HashSet<string>();

                foreach (var (key, _) in instance)
                {
                    if (!definedProps.Contains(key))
                    {
                        errors.Add(new ValidationError
                        {
                            Path = $"{path}.{key}",
                            Message = L.T(StringKey.SchemaAdditionalPropertyNotAllowed, key)
                        });
                    }
                }
            }
        }
    }

    private static void ValidateStringConstraints(JsonNode? instance, JsonObject schema, string path, List<ValidationError> errors)
    {
        if (instance is not JsonValue value || !value.TryGetValue(out string? strValue)) return;

        if (schema.TryGetPropertyValue("minLength", out var minLengthNode))
        {
            var minLength = minLengthNode!.GetValue<int>();
            if (strValue.Length < minLength)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaStringTooShort, strValue.Length, minLength)
                });
            }
        }

        if (schema.TryGetPropertyValue("maxLength", out var maxLengthNode))
        {
            var maxLength = maxLengthNode!.GetValue<int>();
            if (strValue.Length > maxLength)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaStringTooLong, strValue.Length, maxLength)
                });
            }
        }
    }

    private static void ValidateNumberConstraints(JsonNode? instance, JsonObject schema, string path, List<ValidationError> errors)
    {
        if (instance is not JsonValue value) return;

        double? numValue = null;
        if (value.TryGetValue(out int intVal)) numValue = intVal;
        else if (value.TryGetValue(out long longVal)) numValue = longVal;
        else if (value.TryGetValue(out double doubleVal)) numValue = doubleVal;

        if (!numValue.HasValue) return;

        if (schema.TryGetPropertyValue("minimum", out var minimumNode))
        {
            var minimum = minimumNode!.GetValue<double>();
            if (numValue.Value < minimum)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaNumberTooSmall, numValue.Value, minimum)
                });
            }
        }

        if (schema.TryGetPropertyValue("maximum", out var maximumNode))
        {
            var maximum = maximumNode!.GetValue<double>();
            if (numValue.Value > maximum)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaNumberTooLarge, numValue.Value, maximum)
                });
            }
        }
    }

    private void ValidateArray(JsonArray instance, JsonObject schema, string path, List<ValidationError> errors)
    {
        if (schema.TryGetPropertyValue("minItems", out var minItemsNode))
        {
            var minItems = minItemsNode!.GetValue<int>();
            if (instance.Count < minItems)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaArrayTooFewItems, instance.Count, minItems)
                });
            }
        }

        if (schema.TryGetPropertyValue("maxItems", out var maxItemsNode))
        {
            var maxItems = maxItemsNode!.GetValue<int>();
            if (instance.Count > maxItems)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = L.T(StringKey.SchemaArrayTooManyItems, instance.Count, maxItems)
                });
            }
        }

        // Items schema
        if (schema.TryGetPropertyValue("items", out var itemsSchema))
        {
            for (int i = 0; i < instance.Count; i++)
            {
                ValidateNode(instance[i], itemsSchema, $"{path}[{i}]", errors);
            }
        }
    }

    private static string GetJsonTypeName(JsonNode? node)
    {
        if (node is null) return "null";
        if (node is JsonObject) return "object";
        if (node is JsonArray) return "array";
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out bool _)) return "boolean";
            if (value.TryGetValue(out string? _)) return "string";
            if (value.TryGetValue(out double d))
            {
                // 整数判断: 无小数部分且在long范围内
                if (d == Math.Truncate(d) && !double.IsInfinity(d) && Math.Abs(d) <= long.MaxValue)
                {
                    return "integer";
                }
                return "number";
            }
            if (value.TryGetValue(out decimal _)) return "number";
        }
        return "unknown";
    }

    private static bool JsonNodeDeepEquals(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        if (a is JsonValue aValue && b is JsonValue bValue)
        {
            if (aValue.TryGetValue(out string? aStr) && bValue.TryGetValue(out string? bStr))
                return aStr == bStr;
            if (aValue.TryGetValue(out bool aBool) && bValue.TryGetValue(out bool bBool))
                return aBool == bBool;
            if (aValue.TryGetValue(out double aDouble) && bValue.TryGetValue(out double bDouble))
                return Math.Abs(aDouble - bDouble) < 1e-10;
            return false;
        }

        if (a is JsonObject aObj && b is JsonObject bObj)
        {
            if (aObj.Count != bObj.Count) return false;
            foreach (var (key, propValue) in aObj)
            {
                if (!bObj.TryGetPropertyValue(key, out var otherValue)) return false;
                if (!JsonNodeDeepEquals(propValue, otherValue)) return false;
            }
            return true;
        }

        if (a is JsonArray aArr && b is JsonArray bArr)
        {
            if (aArr.Count != bArr.Count) return false;
            for (int i = 0; i < aArr.Count; i++)
            {
                if (!JsonNodeDeepEquals(aArr[i], bArr[i])) return false;
            }
            return true;
        }

        return false;
    }
}

