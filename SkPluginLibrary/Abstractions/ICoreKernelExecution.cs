﻿using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;


namespace SkPluginLibrary.Abstractions;

public interface ICoreKernelExecution
{
    Task<ChatGptPluginManifest> GetManifest(ChatGptPlugin chatGptPlugin);
    Task<Dictionary<string, ISKFunction>> GetExternalPluginFunctions(ChatGptPluginManifest manifest);
    Task<string> ExecuteFunction(ISKFunction function, Dictionary<string, string>? variables = null);
    IAsyncEnumerable<string> ExecuteFunctionStream(ISKFunction function, Dictionary<string, string>? variables = null);
    IAsyncEnumerable<string> ChatWithActionPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null);
    IAsyncEnumerable<string> ChatWithSequentialPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null);
    Task<string> ExecuteFunctionChain(ChatRequestModel chatRequestModel);
    Task<List<PluginFunctions>> GetAllPlugins();

    IAsyncEnumerable<string> ChatWithStepwisePlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null);

    event Action<string>? YieldAdditionalText;
    Task<KernelResult> ExecuteKernelFunction(ISKFunction function, Dictionary<string, string>? variables = null);
}