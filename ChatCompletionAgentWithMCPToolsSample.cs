// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;

namespace MCPClient.Samples;

/// <summary>
/// Demonstrates how to use <see cref="ChatCompletionAgent"/> with MCP tools represented as Kernel functions.
/// </summary>
internal sealed class ChatCompletionAgentWithMCPToolsSample : BaseSample
{
    /// <summary>
    /// Demonstrates how to use <see cref="ChatCompletionAgent"/> with MCP tools represented as Kernel functions.
    /// The code in this method:
    /// 1. Creates an MCP client.
    /// 2. Retrieves the list of tools provided by the MCP server.
    /// 3. Creates a kernel and registers the MCP tools as Kernel functions.
    /// 4. Defines chat completion agent with instructions, name, kernel, and arguments.
    /// 5. Invokes the agent with a prompt.
    /// 6. The agent sends the prompt to the AI model, together with the MCP tools represented as Kernel functions.
    /// 7. The AI model calls DateTimeUtils-GetCurrentDateTimeInUtc function to get the current date time in UTC required as an argument for the next function.
    /// 8. The AI model calls WeatherUtils-GetWeatherForCity function with the current date time and the `Boston` arguments extracted from the prompt to get the weather information.
    /// 9. Having received the weather information from the function call, the AI model returns the answer to the agent and the agent returns the answer to the user.
    /// </summary>
    public static async Task RunAsync()
    {
        Console.WriteLine($"Running the {nameof(ChatCompletionAgentWithMCPToolsSample)} sample.");

        // Create an MCP client
        await using IMcpClient mcpClient = await CreateMcpClientAsync();

        // Retrieve and display the list provided by the MCP server
        IList<McpClientTool> tools = await mcpClient.ListToolsAsync();
        DisplayTools(tools);

        // Create a kernel and register the MCP tools as kernel functions
        Kernel kernel = CreateKernelWithChatCompletionService();
        kernel.Plugins.AddFromFunctions("Tools", tools.Select(t => t.ToKernelFunction(mcpClient)));
        // Enable automatic function calling
        OpenAIPromptExecutionSettings executionSettings = new()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() {})
        };

        string prompt = "make something up to test the create tool";
        Console.WriteLine(prompt);

        // Add function invocation filter to track tool calls
        List<string> calledFunctions = new();
        
        kernel.FunctionInvocationFilters.Add(new FunctionInvocationLoggingFilter(calledFunctions));

        // Define the agent
        ChatCompletionAgent agent = new()
        {
            Instructions = "test mcp stuff.",
            Name = "bob",
            Kernel = kernel,
            Arguments = new KernelArguments(executionSettings),
        };

        // Invokes agent with a prompt
        ChatMessageContent response = await agent.InvokeAsync(prompt).FirstAsync();

        Console.WriteLine(response);
        Console.WriteLine();

        // The expected output is: The sky in Boston today is likely gray due to rainy weather.

        // After the agent invokes
        Console.WriteLine($"\n📊 Summary: {calledFunctions.Count} functions called:");
        foreach (var func in calledFunctions)
        {
            Console.WriteLine($"  - {func}");
        }
    }
}

/// <summary>
/// Filter to log function invocations and track called functions.
/// </summary>
internal sealed class FunctionInvocationLoggingFilter : IFunctionInvocationFilter
{
    private readonly List<string> _calledFunctions;

    public FunctionInvocationLoggingFilter(List<string> calledFunctions)
    {
        _calledFunctions = calledFunctions;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        Console.WriteLine($"🔧 Calling function: {context.Function.PluginName}.{context.Function.Name}");
        Console.WriteLine($"   Arguments: {string.Join(", ", context.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))}");
        _calledFunctions.Add($"{context.Function.PluginName}.{context.Function.Name}");

        // Call the actual function
        await next(context);

        Console.WriteLine($"✅ Function completed: {context.Function.PluginName}.{context.Function.Name}");
        if (context.Result?.GetValue<object>() != null)
        {
            var result = context.Result.GetValue<object>();
            if (result is ModelContextProtocol.Protocol.CallToolResult mcpResult)
            {
                if (mcpResult.Content?.Count > 0)
                {
                    foreach (var content in mcpResult.Content)
                    {
                        // Try to get the actual text content using reflection or ToString with formatting
                        var contentStr = content.ToString();
                        if (contentStr != content.GetType().FullName) // If ToString() gives meaningful output
                        {
                            Console.WriteLine($"   Result: {contentStr}");
                        }
                        else
                        {
                            // Try to access text property dynamically
                            var textProp = content.GetType().GetProperty("Text");
                            if (textProp != null)
                            {
                                var textValue = textProp.GetValue(content);
                                Console.WriteLine($"   Result: {textValue}");
                            }
                            else
                            {
                                Console.WriteLine($"   Result: [Content type: {content.GetType().Name}]");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"   Result: [No content available]");
                }
            }
            else
            {
                Console.WriteLine($"   Result: {result}");
            }
        }
    }
}