using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol;
using System.Text.Json;
using System.Globalization;

namespace MCPClient.Samples;

internal static class McpToolKernelExtensions
{
    public static KernelFunction ToKernelFunction(this McpClientTool tool, IMcpClient client)
        => KernelFunctionFactory.CreateFromMethod(
            async (Kernel kernel, KernelArguments args, CancellationToken ct) =>
            {
                // Convert arguments using a clean, functional approach
                var typedArgs = args.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseValue(kvp.Value)
                );

                var result = await client.CallToolAsync(
                    tool.Name,
                    typedArgs,
                    progress: null,
                    cancellationToken: ct);

                return result;
            },
            functionName: tool.Name,
            description: tool.Description
        );

    /// <summary>
    /// Intelligently converts a value to its most appropriate type.
    /// </summary>
    private static object? ParseValue(object? value) =>
        value switch
        {
            null => null,
            string str when string.IsNullOrEmpty(str) => str,
            string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            string str when bool.TryParse(str, out var b) => b,
            string str => str,
            _ => value
        };
}