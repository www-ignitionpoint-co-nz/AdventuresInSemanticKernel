﻿using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Functions.OpenAPI.Extensions;
using Microsoft.SemanticKernel.Planners;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Memory;
using NCalcPlugins;
using SkPluginLibrary.Plugins;
using System.Net.Http.Json;

namespace SkPluginLibrary;

public partial class CoreKernelService
{
    #region Semantic Plugin Picker (SemanticPluginPicker.razor, SequentialPlannerBuilder.razor)

    public Dictionary<string, ISKFunction> GetPluginFunctions(List<string> pluginName)
    {
        var kernel = CreateKernel();
        var plugin = kernel.ImportSemanticFunctionsFromDirectory(RepoFiles.PluginDirectoryPath, pluginName.ToArray());
        return plugin.ToDictionary(x => x.Key, x => x.Value);
    }

    private Dictionary<string, ISKFunction> GetSemanticFunctions(params string[] pluginNames)
    {
        return GetPluginFunctions(pluginNames.ToList());
    }

    public async Task<ChatGptPluginManifest> GetManifest(ChatGptPlugin chatGptPlugin)
    {
        var client = new HttpClient();
        var manifest = await client.GetFromJsonAsync<ChatGptPluginManifest>(chatGptPlugin.AgentManifestUrl);
        return manifest;
    }

    public async Task<string> ExecuteFunction(ISKFunction function, Dictionary<string, string>? variables = null)
    {
        var kernel = CreateKernel();
        var context = kernel.CreateNewContext();
        foreach (var variable in variables ?? new Dictionary<string, string>())
        {
            context.Variables[variable.Key] = variable.Value;
        }
        var result = await kernel.RunAsync(context.Variables, function);
        return result.Result();
    }

    public async IAsyncEnumerable<string> ExecuteFunctionStream(ISKFunction function, Dictionary<string, string>? variables = null)
    {
        var kernel = CreateKernel();
        var context = kernel.CreateNewContext();
        foreach (var variable in variables ?? new Dictionary<string, string>())
        {
            context.Variables[variable.Key] = variable.Value;
        }
        var result = await kernel.RunAsync(context.Variables, function);
        await foreach (var item in result.ResultStream<string>())
        {
            yield return item;
        }
    }

    private Dictionary<string, object>? _nativePlugins;
    private Dictionary<string, object>? _customNativePlugins;

    private Dictionary<string, object> GetCoreNativePlugins()
    {
        _nativePlugins ??= new Dictionary<string, object>();
        //var result = new Dictionary<string, object>();
        var fileIo = new FileIOPlugin();
        _nativePlugins.TryAdd(nameof(FileIOPlugin), fileIo);
        var http = new HttpPlugin();
        _nativePlugins.TryAdd(nameof(HttpPlugin), http);
        var mathPlugin = new MathPlugin();
        _nativePlugins.TryAdd(nameof(MathPlugin), mathPlugin);
        var timePlugin = new TimePlugin();
        _nativePlugins.TryAdd(nameof(TimePlugin), timePlugin);
        var waitPlugin = new WaitPlugin();
        _nativePlugins.TryAdd(nameof(WaitPlugin), waitPlugin);
        var textMemoryPlugin = new TextMemoryPlugin(_semanticTextMemory);
        _nativePlugins.TryAdd(nameof(TextMemoryPlugin), textMemoryPlugin);
        var textPlugin = new TextPlugin();
        _nativePlugins.TryAdd(nameof(TextPlugin), textPlugin);
        var convoSummaryPlugin = new ConversationSummaryPlugin(CreateKernel());
        _nativePlugins.TryAdd(nameof(ConversationSummaryPlugin), convoSummaryPlugin);
        return _nativePlugins;
    }

    private Dictionary<string, object> GetCustomNativePlugins()
    {
        _customNativePlugins ??= new Dictionary<string, object>();
        //var result = new Dictionary<string, object>();
        var kernel = CreateKernel();
        var simpleCalcPlugin = new SimpleCalculatorPlugin(kernel);
        _customNativePlugins.TryAdd(nameof(SimpleCalculatorPlugin), simpleCalcPlugin);
        var languageCalcPlugin = new LanguageCalculatorPlugin(kernel);
        _customNativePlugins.TryAdd(nameof(LanguageCalculatorPlugin), languageCalcPlugin);
        var csharpPlugin = new ReplCsharpPlugin(kernel);
        _customNativePlugins.TryAdd(nameof(ReplCsharpPlugin), csharpPlugin);
        var webCrawlPlugin = new WebCrawlPlugin(kernel, _bingSearchService);
        _customNativePlugins.TryAdd(nameof(WebCrawlPlugin), webCrawlPlugin);
        var dndPlugin = new DndPlugin();
        _customNativePlugins.TryAdd(nameof(DndPlugin), dndPlugin);
        var jsonPlugin = new HandleJsonPlugin();
        _customNativePlugins.TryAdd(nameof(HandleJsonPlugin), jsonPlugin);
        var streamPlugin = new StreamingPlugin();
        _customNativePlugins.TryAdd(nameof(StreamingPlugin), streamPlugin);
        var wikiPlugin = new WikiChatPlugin(kernel);
        _customNativePlugins.TryAdd(nameof(WikiChatPlugin), wikiPlugin);
        return _customNativePlugins;
    }

    private List<string> CustomNativePluginNames =>
        _customNativePlugins?.Keys.ToList() ?? GetCustomNativePlugins().Keys.ToList();

    private List<string> CoreNativePluginNames => _nativePlugins?.Keys.ToList() ?? GetCoreNativePlugins().Keys.ToList();

    private async Task<Dictionary<string, ISKFunction>> GetApiFunctionsFromNames(List<string> apiPluginNames)
    {
        var result = new Dictionary<string, ISKFunction>();
        var kernel = CreateKernel();
        var tasks = new List<Task<IDictionary<string, ISKFunction>>>();
        foreach (var apiPlugin in apiPluginNames)
        {
            tasks.Add(kernel.ImportPluginFunctionsAsync(apiPlugin,
                Path.Combine(RepoFiles.ApiPluginDirectoryPath, apiPlugin, "openapi.json"), new OpenApiFunctionExecutionParameters { EnableDynamicPayload = true, EnablePayloadNamespacing = true, IgnoreNonCompliantErrors = true }));
            //IDictionary<string, ISKFunction> plugin = await kernel.ImportPluginFunctionsAsync(apiPlugin,
            //    Path.Combine(RepoFiles.ApiPluginDirectoryPath, apiPlugin, "openapi.json"));
            //result = result.AddRange(plugin);
        }

        var taskResults = await Task.WhenAll(tasks);
        return taskResults.Aggregate(result, (current, resultItem) => current.AddRange(resultItem));
    }

    private async Task<Dictionary<string, ISKFunction>> GetApiFunctionsFromNames(params string[] apiPluginNames)
    {
        return await GetApiFunctionsFromNames(apiPluginNames.ToList());
    }

    private Dictionary<string, ISKFunction> GetNativeFunctionsFromNames(List<string> pluginNames, bool isCustom = false)
    {
        var result = new Dictionary<string, ISKFunction>();
        var kernel = CreateKernel();
        var allPlugins = isCustom
            ? _customNativePlugins ?? GetCustomNativePlugins()
            : _nativePlugins ?? GetCoreNativePlugins();
        return pluginNames.Select(plugin => kernel.ImportFunctions(allPlugins[plugin], plugin))
            .Aggregate(result, (current, funcs) => current.AddRange(funcs));
    }

    private Dictionary<string, ISKFunction> GetNativeFunctionsFromNames(bool isCustom = false,
        params string[] pluginNames)
    {
        return GetNativeFunctionsFromNames(pluginNames.ToList(), isCustom);
    }

    private static IEnumerable<string?> SemanticPlugins =>
        Directory.GetDirectories(RepoFiles.PluginDirectoryPath).Select(Path.GetFileName).ToList();

    private static IEnumerable<string?> ApiPlugins => Directory.GetDirectories(RepoFiles.ApiPluginDirectoryPath)
        .Select(Path.GetFileName).ToList();

    public async Task<List<PluginFunctions>> GetAllPlugins()
    {
        var result = new List<PluginFunctions>();
        var coreNative = CoreNativePluginNames.Select(x =>
            GetNativeFunctionsFromNames(false, x).ToPluginFunctions(x, PluginType.Native));
        var customNative = CustomNativePluginNames.Select(x =>
            GetNativeFunctionsFromNames(true, x).ToPluginFunctions(x, PluginType.Native));
        var semantic = SemanticPlugins.Select(x => GetSemanticFunctions(x).ToPluginFunctions(x, PluginType.Semantic));
        result.AddRange(semantic);
        result.AddRange(coreNative);
        result.AddRange(customNative);
        var tasks = ApiPlugins.Where(api => api is not null).Select(GetApiPluginFunctions).ToList();
        var results = await Task.WhenAll(tasks);
        result.AddRange(results);
        return result;
    }

    private async Task<PluginFunctions> GetApiPluginFunctions(string? api)
    {
        if (string.IsNullOrEmpty(api)) throw new ArgumentNullException(nameof(api));
        var pluginFunction = new PluginFunctions(api, PluginType.Api);
        var apiFunctions = await GetApiFunctionsFromNames(api);
        pluginFunction.Functions = apiFunctions.ToFunctionList();
        return pluginFunction;
    }

    public async Task<Dictionary<string, ISKFunction>> GetExternalPluginFunctions(ChatGptPluginManifest manifest)
    {
        var kernel = CreateKernel();
        var uri = string.IsNullOrWhiteSpace(manifest.Api!.PluginUrl)
            ? manifest.Api.Url
            : new Uri(manifest.Api.PluginUrl);
        var executionParameters = new OpenApiFunctionExecutionParameters { IgnoreNonCompliantErrors = true, EnableDynamicPayload = true, EnablePayloadNamespacing = true };
        if (!string.IsNullOrEmpty(manifest.OverrideUrl))
        {
            executionParameters.ServerUrlOverride = new Uri(manifest.OverrideUrl);
        }

        var replaceNonAsciiWithUnderscore = manifest.NameForModel.ReplaceNonAsciiWithUnderscore();
        var plugin = await kernel.ImportPluginFunctionsAsync(replaceNonAsciiWithUnderscore, uri, executionParameters);
        return plugin.ToDictionary(x => x.Key, x => x.Value);
    }

    public async Task<string> ExecuteFunctionChain(ChatRequestModel chatRequestModel)
    {
        var kernel = await CreateKernelWithPlugins(chatRequestModel.SelectedPlugins);
        var context = kernel.CreateNewContext();
        foreach (var variable in chatRequestModel.Variables ?? new Dictionary<string, string>())
        {
            _loggerFactory.CreateLogger<CoreKernelService>().LogInformation("Adding variable {key} with value {value}",
                variable.Key, variable.Value);
            context.Variables[variable.Key] = variable.Value;
        }

        var chain = chatRequestModel.SelectedFunctions.OrderBy(x => x.Order).Select(x => x.SkFunction);
        var result = await kernel.RunAsync(context.Variables, chain.ToArray());
        return result.Result();
    }

    private async Task<ISequentialPlanner> CreateSequentialPlanner(ChatRequestModel requestModel)
    {
        var kernel = await CreateKernelWithPlugins(requestModel.SelectedPlugins);
        var functions = kernel.Functions.GetFunctionViews();

        var config = new SequentialPlannerConfig
        {
            MaxTokens = 2000,
            SemanticMemoryConfig = new SemanticMemoryConfig
            {
                RelevancyThreshold = 0.77,
                MaxRelevantFunctions = 10
            },

        };
        foreach (var exclude in requestModel.ExcludedFunctions ?? new List<string>())
        {
            config.ExcludedFunctions.Add(exclude);
        }

        foreach (var include in requestModel.RequredFunctions ?? new List<string>())
        {
            var pluginname = functions.FirstOrDefault(x => x.Name == include)?.PluginName ?? "";
            config.SemanticMemoryConfig.IncludedFunctions.Add((pluginname, include));
        }

        var planner = new SequentialPlanner(kernel, config).WithInstrumentation(_loggerFactory);
        return planner;
    }


    private record ChatExchange(string UserMessage, string AssistantMessage);

    private readonly List<ChatExchange> _chatExchanges = new();
   
    public async IAsyncEnumerable<string> ChatWithActionPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true)
    {
        var userMessage = $"User: {query}";
        var kernel = await CreateKernelWithPlugins(chatRequestModel.SelectedPlugins);
        var chatService = kernel.GetService<IChatCompletion>();
        var context = kernel.CreateNewContext();
        var planner = new ActionPlanner(kernel);
        yield return "Determining the best action to take...\n\n";
        Plan plan;
        var errorMsg = "";
        var iscreateError = false;
        try
        {
            plan = await planner.CreatePlanAsync(query);
        }
        catch (Exception ex)
        {
            plan = new Plan("");
            iscreateError = true;
            errorMsg = ex.ToString();
        }

        if (iscreateError)
        {
            var errorSystemPrompt =
                $"The upcoming User's Query generated an error creating a Semantic Kernel Plan using the Action Planner. Apologize for the error and provide the best explantaion you can for the error.\n[Error]\n{errorMsg}";
            var errorChat = chatService.CreateNewChat(errorSystemPrompt);
            await foreach (var token in chatService.GenerateMessageStreamAsync(errorChat))
            {
                yield return token;
            }

            yield break;
        }

        var skillName = plan.Steps[0].PluginName ?? plan.PluginName;
        var name = plan.Steps[0].Name ?? plan.Name;
        yield return $"Executing Function **{name}** from plugin {skillName}\n\n";
        string? result;
        try
        {
            var planResult = await plan.InvokeAsync(context);
            result = planResult.Result();
        }
        catch (Exception ex)
        {
            result = ex.ToString();
        }

        if (!runAsChat)
        {
            yield return $"### Final Result\n\n{result}";
            yield break;
        }

        var systemPrmpt =
            $"You are a helpful AI chatbot. Respond to the user to the best of your ability using the following Context. If Context indicates an error. Apologize for the error and explain the error as well as possible.\n\n[Context]\n{result}";
        var chat = chatService.CreateNewChat(systemPrmpt);
        foreach (var exchange in _chatExchanges)
        {
            chat.AddUserMessage(exchange.UserMessage);
            chat.AddAssistantMessage(exchange.AssistantMessage);
        }

        chat.AddUserMessage(query);
        var response = "";
        await foreach (var token in chatService.GenerateMessageStreamAsync(chat))
        {
            response += token;
            yield return token;
        }

        var assistantMessage = $"Assistant: {response}";
        _chatExchanges.Add(new ChatExchange(userMessage, assistantMessage));
    }

    public async IAsyncEnumerable<string> ChatWithSequentialPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true)
    {
        var kernel = CreateKernel();

        var planner = await CreateSequentialPlanner(chatRequestModel);
        yield return "## Generating Plan\n\n";
        var (plan, skillStepsHeader, isError) = await GeneratePlanAsync(query, planner);


        yield return skillStepsHeader;
        if (isError)
        {
            yield break;
        }

        var stepNumber = 0;
        foreach (var step in plan.Steps)
        {
            yield return $"| {++stepNumber} | {step.Name} | {step.PluginName} | {step.Description} |\n";
        }

        yield return "\nExecuting plan...\n\n";
        stepNumber = 0;
        //var something = plan.ExecutePlanBySteps(kernel.CreateNewContext())
        var finalResult = "";
        var context = kernel.CreateNewContext();
        foreach (var input in chatRequestModel.Variables ?? new Dictionary<string, string>())
        {
            context.Variables[input.Key] = input.Value;
        }

        while (plan.HasNextStep)
        {
            var hasError = false;
            yield return $"### Step {++stepNumber}\n";
            string? stepResultString;
            try
            {
                var stepResult = await plan.InvokeNextStepAsync(context);
                stepResultString = stepResult.State.ToString();
                finalResult = stepResult.State.ToString();
            }
            catch (SKException ex)
            {
                stepResultString = $"Error on step {stepNumber}\n\n {ex.Message}\n\n{ex.StackTrace}";
                hasError = true;
            }

            yield return $"{stepResultString}\n\n";
            if (hasError) break;
        }

        //var planResult = await plan.InvokeAsync(loggerFactory: _loggerFactory);
        var result = finalResult;
        if (runAsChat)
        {
            yield return "### Execution Complete\n\n#### Chat:\n\n";
            await foreach (var p in ExecuteChatStream(query, result, kernel))
                yield return p;
        }
        else
        {
            yield return "### Execution Complete";
        }
    }

    private async Task<(Plan plan, string skillStepsHeader, bool isError)> GeneratePlanAsync(string query,
        ISequentialPlanner planner)
    {
        Plan plan;
        var skillStepsHeader = "| Step | Function | Skill | Description |\n|:---:|:---:|:---:|:---:|\n";
        var isError = false;
        try
        {
            plan = await planner.CreatePlanAsync(query);
            _loggerFactory.CreateLogger<CoreKernelService>()
                .LogInformation("Plan:\n{plan.ToPlanString()}", plan.ToPlanString());
            Console.WriteLine($"Plan:\n{plan.ToPlanString()}");
        }
        catch (Exception ex)
        {
            plan = new Plan(query);
            skillStepsHeader = ex.ToString();
            isError = true;
        }

        return (plan, skillStepsHeader, isError);
    }

    private async IAsyncEnumerable<string> ExecuteChatStream(string query, string result, IKernel kernel)
    {
        var systemPrmpt =
            $"""
                You are "Adventures in Semantic Kernal" a helpful, intelligent and resourceful AI chatbot. Respond to the user to the best of your ability using the following Context. If Context indicates an error. Aplogize for the error and explain the error as well as possible.
                The following [Context] is based on the user's query and with in a medium with access to real-time information and web access. Assume [Context] was generated with access to real-time information. Use the [Context] along with the query to respond. Respond with the exact language in [Context] when appropriate.
                NEVER REVEAL THAT ANY CONTEXT WAS PROVIDED.
                [Context]
                {result}
                """;
        var chatService = kernel.GetService<IChatCompletion>();
        var chat = chatService.CreateNewChat(systemPrmpt);
        foreach (var exchange in _chatExchanges)
        {
            chat.AddUserMessage(exchange.UserMessage);
            chat.AddAssistantMessage(exchange.AssistantMessage);
        }

        var settings = new OpenAIRequestSettings { Temperature = 1.0, TopP = 1.0, MaxTokens = 3000 };
        chat.AddUserMessage(query);
        var response = "";
        await foreach (var token in chatService.GenerateMessageStreamAsync(chat, settings))
        {
            response += token;
            yield return token;
        }

        var userMessage = $"User: {query}";
        var assistantMessage = $"Assistant: {response}";
        _chatExchanges.Add(new ChatExchange(userMessage, assistantMessage));
    }

    private async Task<IKernel> CreateKernelWithPlugins(IReadOnlyCollection<PluginFunctions> pluginFunctions)
    {
        var kernel = CreateKernel();
        var semanticPlugins = kernel.ImportSemanticFunctionsFromDirectory(RepoFiles.PluginDirectoryPath,
            pluginFunctions.Where(x => x.PluginType == PluginType.Semantic).Select(x => x.PluginName).ToArray());
        foreach (var apiPlugin in pluginFunctions.Where(x => x.PluginType == PluginType.Api))
        {
            var api = await kernel.ImportPluginFunctionsAsync(apiPlugin.PluginName,
                Path.Combine(RepoFiles.ApiPluginDirectoryPath, apiPlugin.PluginName, "openapi.json"));
        }

        var natives = pluginFunctions.Where(x => x.PluginType == PluginType.Native)
            .SelectMany(x => x.SkFunctions.Values).ToList();
        natives.ForEach(x => kernel.RegisterCustomFunction(x));
        return kernel;
    }


    #endregion
}