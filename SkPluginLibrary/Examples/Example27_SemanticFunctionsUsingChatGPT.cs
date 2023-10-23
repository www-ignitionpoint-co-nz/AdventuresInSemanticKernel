﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel;

namespace SkPluginLibrary.Examples;

/**
 * This example shows how to use GPT3.5 Chat model for prompts and semantic functions.
 */
// ReSharper disable once InconsistentNaming
public static class Example27_SemanticFunctionsUsingChatGPT
{
    public static async Task RunAsync()
    {
        Console.WriteLine("======== Using Chat GPT model for text completion ========");

        IKernel kernel = new KernelBuilder()
            .WithLoggerFactory(ConsoleLogger.LoggerFactory)
            .WithOpenAIChatCompletionService(TestConfiguration.OpenAI.ModelId, TestConfiguration.OpenAI.ApiKey, alsoAsTextCompletion: true)
            .Build();

        var func = kernel.CreateSemanticFunction(
            "List the two planets closest to '{{$input}}', excluding moons, using bullet points.");

        var result = await func.InvokeAsync("Jupiter", kernel);
        Console.WriteLine(result.GetValue<string>());

        /*
    Output:
       - Saturn
       - Uranus
    */
    }
}