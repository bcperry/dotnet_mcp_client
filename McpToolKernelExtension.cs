using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol;
using System.Text.Json;
using System.Globalization;

namespace MCPClient.Samples;

internal static class McpToolKernelExtensions
{
    public static KernelFunction ToKernelFunction(this McpClientTool tool, IMcpClient client)
    {
        // Extract parameter information from the schema for better AI understanding
        var parameters = new List<KernelParameterMetadata>();
        
        try
        {
            if (tool.JsonSchema.ValueKind != JsonValueKind.Null && 
                tool.JsonSchema.TryGetProperty("properties", out var properties))
            {
                // Get required fields
                var requiredFields = new HashSet<string>();
                if (tool.JsonSchema.TryGetProperty("required", out var requiredArray))
                {
                    foreach (var element in requiredArray.EnumerateArray())
                    {
                        requiredFields.Add(element.GetString() ?? "");
                    }
                }

                // Process each property
                foreach (var prop in properties.EnumerateObject())
                {
                    var propName = prop.Name;
                    var propValue = prop.Value;
                    
                    string description = "";
                    string type = "string";
                    bool isArray = false;
                    
                    if (propValue.TryGetProperty("description", out var descElement))
                    {
                        description = descElement.GetString() ?? "";
                    }
                    
                    // Handle direct type property
                    if (propValue.TryGetProperty("type", out var typeElement))
                    {
                        type = typeElement.GetString() ?? "string";
                        if (type == "array")
                        {
                            isArray = true;
                            type = "string"; // Arrays will be handled as comma-separated strings for LLM compatibility
                        }
                    }
                    
                    // Handle anyOf schemas (nullable types)
                    if (propValue.TryGetProperty("anyOf", out var anyOfElement) && anyOfElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var anyOfItem in anyOfElement.EnumerateArray())
                        {
                            if (anyOfItem.TryGetProperty("type", out var anyOfTypeElement))
                            {
                                var anyOfType = anyOfTypeElement.GetString();
                                if (anyOfType != "null") // Skip null types
                                {
                                    type = anyOfType ?? "string";
                                    if (type == "array")
                                    {
                                        isArray = true;
                                        type = "string"; // Arrays will be handled as comma-separated strings
                                    }
                                    break; // Use the first non-null type
                                }
                            }
                        }
                    }
                    
                    // Adjust description for arrays
                    if (isArray && !description.Contains("array") && !description.Contains("comma"))
                    {
                        description += " (provide as comma-separated values)";
                    }
                    
                    var param = new KernelParameterMetadata(propName)
                    {
                        Description = description,
                        IsRequired = requiredFields.Contains(propName),
                        ParameterType = type switch
                        {
                            "integer" => typeof(int),
                            "number" => typeof(double),
                            "boolean" => typeof(bool),
                            _ => typeof(string)
                        }
                    };
                    
                    parameters.Add(param);
                }
            }
        }
        catch (Exception)
        {
            // If schema parsing fails, use a simple approach
        }

        return KernelFunctionFactory.CreateFromMethod(
            async (Kernel kernel, KernelArguments args, CancellationToken ct) =>
            {
                // Convert arguments using the tool's schema for proper type coercion
                var typedArgs = args.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseValueWithSchema(kvp.Value, kvp.Key, tool)
                );

                // Debug: Log the arguments being sent to the server
                Console.WriteLine($"🔍 Sending to server: {System.Text.Json.JsonSerializer.Serialize(typedArgs, new JsonSerializerOptions { WriteIndented = true })}");

                var result = await client.CallToolAsync(
                    tool.Name,
                    typedArgs,
                    progress: null,
                    cancellationToken: ct);

                return result;
            },
            functionName: tool.Name,
            description: tool.Description,
            parameters: parameters,
            returnParameter: new KernelReturnParameterMetadata { Description = "The result of the operation" }
        );
    }

    /// <summary>
    /// Converts a value to the appropriate type based on the tool's schema.
    /// </summary>
    private static object? ParseValueWithSchema(object? value, string parameterName, McpClientTool tool)
    {
        if (value == null) return null;
    
        
        // Try to get the expected type from the tool's schema
        try
        {
            if (tool.JsonSchema.ValueKind != JsonValueKind.Null && 
                tool.JsonSchema.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty(parameterName, out var schemaProperty))
            {
                var expectedType = GetSchemaType(schemaProperty);
                var isArray = IsArrayType(schemaProperty);
                
                if (isArray && value is string arrayStr && !string.IsNullOrEmpty(arrayStr))
                {
                    // Convert comma-separated string to actual array
                    var items = arrayStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim())
                                      .ToArray();
                    return items;
                }
                
                return value switch
                {
                    string str when string.IsNullOrEmpty(str) => str,
                    string str when expectedType == "integer" && int.TryParse(str, out var intVal) => intVal,
                    string str when expectedType == "number" && double.TryParse(str, out var doubleVal) => doubleVal,
                    string str when expectedType == "boolean" && bool.TryParse(str, out var boolVal) => boolVal,
                    string str => str,
                    _ => value
                };
            }
        }
        catch (Exception)
        {
            // If schema parsing fails, fall back to minimal conversion
        }
        
        // Fallback to minimal conversion if no schema info available
        return value switch
        {
            null => null,
            string str when string.IsNullOrEmpty(str) => str,
            string str when bool.TryParse(str, out var b) => b,
            string str => str,
            _ => value
        };
    }
    
    /// <summary>
    /// Gets the type from a JSON schema property, handling anyOf patterns.
    /// </summary>
    private static string GetSchemaType(JsonElement schemaProperty)
    {
        // Handle direct type property
        if (schemaProperty.TryGetProperty("type", out var typeElement))
        {
            return typeElement.GetString() ?? "string";
        }
        
        // Handle anyOf schemas (nullable types)
        if (schemaProperty.TryGetProperty("anyOf", out var anyOfElement) && anyOfElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var anyOfItem in anyOfElement.EnumerateArray())
            {
                if (anyOfItem.TryGetProperty("type", out var anyOfTypeElement))
                {
                    var anyOfType = anyOfTypeElement.GetString();
                    if (anyOfType != "null") // Skip null types
                    {
                        return anyOfType ?? "string";
                    }
                }
            }
        }
        
        return "string";
    }
    
    /// <summary>
    /// Checks if a JSON schema property represents an array type.
    /// </summary>
    private static bool IsArrayType(JsonElement schemaProperty)
    {
        // Handle direct type property
        if (schemaProperty.TryGetProperty("type", out var typeElement) && 
            typeElement.GetString() == "array")
        {
            return true;
        }
        
        // Handle anyOf schemas (nullable types)
        if (schemaProperty.TryGetProperty("anyOf", out var anyOfElement) && anyOfElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var anyOfItem in anyOfElement.EnumerateArray())
            {
                if (anyOfItem.TryGetProperty("type", out var anyOfTypeElement) &&
                    anyOfTypeElement.GetString() == "array")
                {
                    return true;
                }
            }
        }
        
        return false;
    }
}