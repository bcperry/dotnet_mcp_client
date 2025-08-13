// Copyright (c) Microsoft. All rights reserved.

using System.Threading.Tasks;


namespace MCPClient.Samples;

internal sealed class Program
{
    /// <summary>
    /// Main method to run all the samples.
    /// </summary>
    public static async Task Main(string[] args)
    {

        await ChatCompletionAgentWithMCPToolsSample.RunAsync();

    }
}
