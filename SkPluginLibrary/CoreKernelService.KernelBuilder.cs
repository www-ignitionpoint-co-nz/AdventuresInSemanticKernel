﻿using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using NCalcPlugins;
using SkPluginLibrary.Plugins;
using System.Net.Http.Json;
using System.Text.Json;
using SkPluginComponents;
using SkPluginComponents.Models;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;
using Microsoft.SemanticKernel.Plugins.OpenApi;
using Microsoft.SemanticKernel.Planning;

namespace SkPluginLibrary;

public partial class CoreKernelService
{
    private const string SkillStepsHeader = "<tr><th>Name</th> <th>Value</th></tr>";

    #region Semantic Plugin Picker (SemanticPluginPicker.razor, SequentialPlannerBuilder.razor)

    public Dictionary<string, KernelFunction> GetPluginFunctions(List<string> pluginName)
    {
        var result = new Dictionary<string, KernelFunction>();
        var kernel = CreateKernel();
        foreach (var plugin in pluginName)
        {
            var functions = kernel.ImportPluginFromPromptDirectory(Path.Combine(RepoFiles.PluginDirectoryPath, plugin));
            result.UpsertConcat(functions.ToDictionary(x => x.Name, x => x));
        }

        //var plugin = kernel.ImportPluginFromPromptDirectory(RepoFiles.PluginDirectoryPath, pluginName.ToArray());
        return result;
    }

    private Dictionary<string, KernelFunction> GetSemanticFunctions(params string[] pluginNames)
    {
        return GetPluginFunctions(pluginNames.ToList());
    }

    public async Task<ChatGptPluginManifest> GetManifest(ChatGptPlugin chatGptPlugin)
    {
        var client = new HttpClient();
        var manifest = await client.GetFromJsonAsync<ChatGptPluginManifest>(chatGptPlugin.AgentManifestUrl);
        return manifest;
    }

    public async Task<FunctionResult> ExecuteKernelFunction(KernelFunction function, Dictionary<string, string>? variables = null)
    {
        var kernel = CreateKernel();
        var context = new KernelArguments();
        if (variables is not null)
        {
            context = new KernelArguments();
        }
        foreach (var item in variables)
        {
            context[item.Key] = item.Value;
        }

        var result = await kernel.InvokeAsync(function, context);
        return result;
    }
    public async IAsyncEnumerable<string> ExecuteFunctionStream(KernelFunction function, Dictionary<string, string>? variables = null)
    {
        var kernel = CreateKernel("gpt-4-1106-preview");
        var context = new KernelArguments();
        if (variables is not null)
        {
            context = new KernelArguments(variables.ToDictionary(x => x.Key, x => (object)x.Value)!);
        }
        var result = await kernel.InvokeAsync(function, context);
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
        var convoSummaryPlugin = new ConversationSummaryPlugin();
        _nativePlugins.TryAdd(nameof(ConversationSummaryPlugin), convoSummaryPlugin);
        var bingConnector = new BingConnector(TestConfiguration.Bing.ApiKey);
        var webSearchPlugin = new WebSearchEnginePlugin(bingConnector);
        _nativePlugins.TryAdd(nameof(WebSearchEnginePlugin), webSearchPlugin);
        var searchUrlPlugin = new SearchUrlPlugin();
        _nativePlugins.TryAdd(nameof(SearchUrlPlugin), searchUrlPlugin);
        return _nativePlugins;
    }

    private Dictionary<string, object> GetCustomNativePlugins()
    {
        _customNativePlugins ??= new Dictionary<string, object>();
        //var result = new Dictionary<string, object>();
        var kernel = CreateKernel();
        //var simpleCalcPlugin = new SimpleCalculatorPlugin();
        //_customNativePlugins.TryAdd(nameof(SimpleCalculatorPlugin), simpleCalcPlugin);
        var languageCalcPlugin = new LanguageCalculatorPlugin();
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
        var youtubePlugin = new YouTubePlugin(kernel, _configuration["YouTubeSearch:ApiKey"]!);
        _customNativePlugins.TryAdd(nameof(YouTubePlugin), youtubePlugin);
        var askUserPlugin = new AskUserPlugin(_modalService);
        _customNativePlugins.TryAdd(nameof(AskUserPlugin), askUserPlugin);
        var blazorChatPlugin = new BlazorMemoryPlugin();
        _customNativePlugins.TryAdd(nameof(BlazorMemoryPlugin), blazorChatPlugin);
        var chatWithSkPlugin = new KernelChatPlugin();
        _customNativePlugins.TryAdd(nameof(KernelChatPlugin), chatWithSkPlugin);
        var langchainChatPlugin = new LangchainChatPlugin();
        _customNativePlugins.TryAdd(nameof(LangchainChatPlugin), langchainChatPlugin);
        return _customNativePlugins;
    }

    private IEnumerable<string> CustomNativePluginNames =>
        _customNativePlugins?.Keys.ToList() ?? GetCustomNativePlugins().Keys.ToList();

    private IEnumerable<string> CoreNativePluginNames => _nativePlugins?.Keys.ToList() ?? GetCoreNativePlugins().Keys.ToList();

    private async Task<Dictionary<string, KernelFunction>> GetApiFunctionsFromNames(IEnumerable<string> apiPluginNames)
    {
        var kernel = CreateKernel();
        var tasks = apiPluginNames.Select(apiPlugin => kernel.ImportPluginFromOpenApiAsync(apiPlugin, Path.Combine(RepoFiles.ApiPluginDirectoryPath, apiPlugin, "openapi.json"), new OpenApiFunctionExecutionParameters { EnableDynamicPayload = true, EnablePayloadNamespacing = true, IgnoreNonCompliantErrors = true })).ToList();

        var taskResults = await Task.WhenAll(tasks);
        var functionsResult = taskResults.SelectMany(x => x).ToDictionary(x => x.Name, x => x, EqualityComparer<string>.Default);
        return functionsResult;
    }

    private async Task<Dictionary<string, KernelFunction>> GetApiFunctionsFromNames(params string[] apiPluginNames)
    {
        return await GetApiFunctionsFromNames(apiPluginNames.ToList());
    }

    private Dictionary<string, KernelFunction> GetNativeFunctionsFromNames(IEnumerable<string> pluginNames, bool isCustom = false)
    {
        var result = new Dictionary<string, KernelFunction>();
        var kernel = CreateKernel();
        var allPlugins = isCustom
            ? _customNativePlugins ?? GetCustomNativePlugins()
            : _nativePlugins ?? GetCoreNativePlugins();
        Dictionary<string, KernelFunction> result1 = result;
        foreach (var plugin in pluginNames)
        {
            var kernelPlugin = kernel.ImportPluginFromObject(allPlugins[plugin], plugin);
            result1 = result1.UpsertConcat(kernelPlugin.ToDictionary(x => x.Name, x => x));
        }

        return result1;
    }

    private Dictionary<string, KernelFunction> GetNativeFunctionsFromNames(bool isCustom = false,
        params string[] pluginNames)
    {
        return GetNativeFunctionsFromNames(pluginNames.ToList(), isCustom);
    }

    private static IEnumerable<string?> SemanticPlugins =>
        Directory.GetDirectories(RepoFiles.PluginDirectoryPath).Select(Path.GetFileName).ToList();

    private static IEnumerable<string?> ApiPlugins => Directory.GetDirectories(RepoFiles.ApiPluginDirectoryPath)
        .Select(Path.GetFileName).ToList();

    public async Task<Dictionary<PluginType, List<KernelPlugin>>> GetAllPlugins()
    {
        var result = new Dictionary<PluginType, List<KernelPlugin>>
        {
            { PluginType.Prompt, []}, { PluginType.Native, []}, { PluginType.Api, []}
        };
        //var semantic = SemanticPlugins.Select(x => GetSemanticFunctions(x).ToPlugin(x, PluginType.Prompt));
        var promptPlugins = SemanticPlugins.Select(name => Kernel.CreateBuilder().Build().ImportPluginFromPromptDirectoryYaml(name)).ToList();
        var semanicPlugins = new List<KernelPlugin>();
        result[PluginType.Prompt] = promptPlugins;
        var coreNative = CoreNativePluginNames.Select(x =>
            GetNativeFunctionsFromNames(false, x).ToPlugin(x, PluginType.Native));
        var customNative = CustomNativePluginNames.Select(x =>
            GetNativeFunctionsFromNames(true, x).ToPlugin(x, PluginType.Native));
        var natives = coreNative.Concat(customNative).ToList();
        result[PluginType.Native] = natives;

        var tasks = ApiPlugins.Where(api => api is not null).Select(GetApiPluginFunctions).ToList();
        var apiResults = await Task.WhenAll(tasks);
        result[PluginType.Api] = [.. apiResults];
        return result;
    }

    private Task<KernelPlugin> GetApiPluginFunctions(string? api)
    {
        if (string.IsNullOrEmpty(api)) throw new ArgumentNullException(nameof(api));
        return Kernel.CreateBuilder().Build().ImportPluginFromOpenApiAsync(Path.GetFileName(api), Path.Combine(RepoFiles.ApiPluginDirectoryPath, api, "openapi.json"), new OpenApiFunctionExecutionParameters { EnableDynamicPayload = true, IgnoreNonCompliantErrors = true, EnablePayloadNamespacing = true });

    }

    public async Task<Dictionary<string, KernelFunction>> GetExternalPluginFunctions(ChatGptPluginManifest manifest)
    {
        var kernel = CreateKernel();
        var uri = manifest.Api.Url;
        var executionParameters = new OpenAIFunctionExecutionParameters { IgnoreNonCompliantErrors = true, EnableDynamicPayload = true, EnablePayloadNamespacing = true };
        if (!string.IsNullOrEmpty(manifest.OverrideUrl))
        {
            executionParameters.ServerUrlOverride = new Uri(manifest.OverrideUrl);
        }

        var replaceNonAsciiWithUnderscore = manifest.NameForModel.ReplaceNonAsciiWithUnderscore();
        KernelPlugin? plugin = null;
        try
        {
            plugin = await kernel.ImportPluginFromOpenApiAsync(replaceNonAsciiWithUnderscore, uri, executionParameters);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw new Exception($"Error importing plugin from {uri}", ex);
        }

        var functionNames = plugin.GetFunctionsMetadata().Select(x => x.Name).ToList();
        var result = new Dictionary<string, KernelFunction>();
        foreach (var functionName in functionNames)
        {
            var function = plugin.TryGetFunction(functionName, out var func) ? func : null;
            if (function is null) continue;
            result.Add(functionName, function);
        }
        return result;
    }
    private void FunctionInvokingHandler(object? sender, FunctionInvokingEventArgs e)
    {
        var function = e.Function;
        var originalValues = e.Metadata;
        Console.WriteLine($"Executing {function.Metadata.PluginName}.{function.Name}.\nVariables:\n{string.Join("\n", originalValues?.Select(x => $"Name: {x.Key}, Value: {x.Value}") ?? new List<string>())}");
    }

    private void FunctionInvokedHandler(object? sender, FunctionInvokedEventArgs args)
    {

    }

    private async Task<HandlebarsPlanner> CreateHandlebarsPlanner(ChatRequestModel requestModel)
    {
        var kernel = await CreateKernelWithPlugins(requestModel.SelectedPlugins);
        var functions = kernel.Plugins.SelectMany(x => x).ToList();
        kernel.FunctionInvoked += FunctionInvokedHandler;
        var config = new HandlebarsPlannerOptions
        {
            MaxTokens = 2000,
            AllowLoops = true,
        };
        foreach (var exclude in requestModel.ExcludedFunctions ?? new List<string>())
        {
            config.ExcludedFunctions.Add(exclude);
        }

        //foreach (var include in requestModel.RequredFunctions ?? new List<string>())
        //{
        //    var pluginname = functions.FirstOrDefault(x => x.Name == include)?.Metadata.PluginName ?? "";
        //    config.IncludedFunctions.Add((pluginname, include));
        //}
        var planner = new HandlebarsPlanner(config);
        return planner;
    }

    private record ChatExchange(string UserMessage, string AssistantMessage);

    private readonly List<ChatExchange> _chatExchanges = new();
    private ChatHistory _chatHistory = new();

    public async IAsyncEnumerable<string> ChatWithAutoFunctionCalling(string query, ChatRequestModel requestModel,
        bool runAsChat = true, string? askOverride = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        //yield return "stop";
        var userMessage = $"User: {query}";
        var kernel = await CreateKernelWithPlugins(requestModel.SelectedPlugins);
        //var chatService = kernel.GetService<IChatCompletion>();
        //var context = kernel.CreateNewContext();
        //var functions = kernel.Functions.GetFunctionViews();
        kernel.FunctionInvoking += (_, e) =>
        {
            var soemthing = e.Function;
            YieldAdditionalText?.Invoke($"\n<h4> Executing {soemthing.Name} {soemthing.Metadata.PluginName}</h4>\n\n");
        };
        kernel.FunctionInvoked += HandleFunctionInvoked;
        var settings = new OpenAIPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions };
        if (!runAsChat)
        {
            await foreach (var update in kernel.InvokePromptStreamingAsync(query, new KernelArguments(settings), cancellationToken: cancellationToken))
            {
                yield return update.ToString();
            }
        }
        else
        {
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            _chatHistory.AddUserMessage(query);
            var assistantMessage = "";
            await foreach (var update in chat.GetStreamingChatMessageContentsAsync(_chatHistory, settings, kernel, cancellationToken))
            {
                assistantMessage += update.Content;
                yield return update.Content;
            }
            _chatHistory.AddAssistantMessage(assistantMessage);
        }

    }
    private static StepwiseExecutionResult _executionResults = new();
    private readonly AskUserService _modalService;
    private readonly JsonSerializerOptions _optionsAsIndented = JsonExtensions.JsonOptionsIndented;
    public event Action<string>? YieldAdditionalText;
    public async IAsyncEnumerable<string> ChatWithStepwisePlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        //yield return "no! No Stepwise Planner!";
        var kernel = await CreateKernelWithPlugins(chatRequestModel.SelectedPlugins);
        var config = new FunctionCallingStepwisePlannerConfig
        {
            MaxIterations = 15,
            MaxTokens = 5500,
            SemanticMemoryConfig = new()
            {
                RelevancyThreshold = 0.77,
                MaxRelevantFunctions = 20
            },
        };
        var planner = new FunctionCallingStepwisePlanner(config);

        yield return "<h2>Generating Plan...</h2><br/>";
        askOverride ??= query;


        yield return SkillStepsHeader;
        //if (isError)
        //{
        //    yield break;
        //}



        yield return "\n<h3>Executing plan...</h3>\n\n";
        var finalResult = "";
        kernel.FunctionInvoking += (_, e) =>
        {
            var soemthing = e.Function;
            YieldAdditionalText?.Invoke($"\n<h4>Executing {soemthing.Name} {soemthing.Metadata.PluginName}</h4>\n\n");
        };
        kernel.FunctionInvoked += HandleFunctionInvoked;

        //var result = await kernel.InvokeAsync(plan, cancellationToken: cancellationToken);
        //var executionResult = result.AsStepwiseExecutionResult(config);
        var planResults = await planner.ExecuteAsync(kernel, askOverride, cancellationToken);
        var chatResult = planResults.ChatHistory;
        yield return "<h4>Internal Chat:</h4>";
        yield return "<ol>";
        foreach (var chat in chatResult)
        {
            yield return $"<li>{chat.Role} - {chat.Content}</li>";
        }
        yield return "</ol>";
        finalResult = planResults.FinalAnswer;

        if (runAsChat)
        {
            yield return "<h3> Execution Complete</h3><br/><h4> Chat:</h4><hr/>";
            await foreach (var p in ExecuteChatStream(query, finalResult, kernel).WithCancellation(cancellationToken))
                yield return p;
        }
        else
        {
            yield return "<h3> Execution Complete<h3>";
            yield return "<h4> Final Result<h4>";
            yield return finalResult;
        }
    }
    public async IAsyncEnumerable<string> ChatWithHandlebarsPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var kernel = CreateKernel("gpt-4-1106-preview");
        kernel.FunctionInvoked += HandleFunctionInvoked;

        var planner = await CreateHandlebarsPlanner(chatRequestModel);
        yield return "<h2> Generating Plan</h2>\n\n";
        HandlebarsPlan? plan = null;
        var isError = false;
        var ask = askOverride ?? query;
        try
        {
            plan = await planner.CreatePlanAsync(kernel, ask, cancellationToken);
        }
        catch (Exception ex)
        {
            isError = true;
        }
        var skillStepsHeader = SkillStepsHeader;
        var arguments = chatRequestModel.Variables;
        var kernelArgs = new KernelArguments((arguments?.ToDictionary(x => x.Key, x => (object)x.Value) ?? new Dictionary<string, object>())!);
        yield return skillStepsHeader;
        if (isError)
        {
            yield break;
        }
        yield return "\n<h3>Executing plan...</h3>\n\n";
        var finalResult = "";
        kernel.FunctionInvoking += (_, e) =>
        {
            var soemthing = e.Function.Metadata;
            YieldAdditionalText?.Invoke($"\n<h4> Executing {soemthing.Name} {soemthing.PluginName}</h4>\n\n");
        };
        kernel.FunctionInvoked += HandleFunctionInvoked;
        Console.WriteLine($"Handlebars plan: {plan}");
        var result = await plan!.InvokeAsync(kernel, kernelArgs, cancellationToken);

        finalResult = result;
        if (runAsChat)
        {
            yield return "<h3> Execution Complete\n\n</h3> Chat:<br/>";
            await foreach (var p in ExecuteChatStream(query, finalResult, kernel).WithCancellation(cancellationToken))
                yield return p;
        }
        else
        {
            yield return "<h3> Execution Complete</h3>";
            yield return "<h4> Final Result</h4>\n";
            yield return finalResult;
        }

        //var metaData =
        //    $"<strong>{JsonSerializer.Serialize<Dictionary<string, object>>(result.FunctionResults.FirstOrDefault()?.Metadata, _optionsAsIndented)}</strong>";
        //var metaDataExpando = $"""

        //                       <details>
        //                         <summary>See Metadata</summary>

        //                         <h5>Metadata</h5>
        //                         <p>
        //                         {metaData}
        //                         </p>
        //                         <br/>
        //                       </details>
        //                       """;
        //yield return metaDataExpando;
        var planDeets = plan.ToString();
        var planDeetsExpando = $"""

                               <details>
                                 <summary>See Plan</summary>
                                 
                                 <h5>Plan</h5>
                                 <p>
                                 {planDeets}
                                 </p>
                                 <br/>
                               </details>
                               """;
        yield return planDeetsExpando;
    }

    private void HandleFunctionInvoked(object? sender, FunctionInvokedEventArgs invokedArgs)
    {
        var function = invokedArgs.Function;

        YieldAdditionalText?.Invoke($"\n<h4> {function.Name} {function.Metadata.PluginName} Completed</h4>\n\n");
        var result = $"<p>{invokedArgs.Result}</p>";
        var resultsExpando = $"""

                              <details>
                                <summary>See Results</summary>
                                
                                <h5>Results</h5>
                                <p>
                                {result}
                                </p>
                                <br/>
                              </details>
                              """;

        YieldAdditionalText?.Invoke(resultsExpando);
        var metaData =
            $"<strong>{JsonSerializer.Serialize(invokedArgs.Metadata, _optionsAsIndented)}</strong>";
        var metaDataExpando = $"""

                               <details>
                                 <summary>See Metadata</summary>
                                 
                                 <h5>Metadata</h5>
                                 <p>
                                 {metaData}
                                 </p>
                                 <br/>
                               </details>
                               """;
        YieldAdditionalText?.Invoke(metaDataExpando);
    }
    [Obsolete("No,no to Sequential planner")]
    public async IAsyncEnumerable<string> ChatWithSequentialPlanner(string query, ChatRequestModel chatRequestModel,
        bool runAsChat = true, string? askOverride = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return "stop";
        //var kernel = CreateKernel("gpt-4-1106-preview");
        //kernel.FunctionInvoked += FunctionInvokedHandler;
        //var planner = await CreateSequentialPlanner(chatRequestModel);
        ////var planner = await CreateStepwisePlanner(chatRequestModel);
        //yield return "<h2> Generating Plan</h2>\n\n";
        //askOverride ??= query;
        //var (plan, skillStepsHeader, isError) = await GeneratePlanAsync(askOverride, planner);

        //yield return "<table>";
        //yield return skillStepsHeader;
        //if (isError)
        //{
        //    yield break;
        //}

        //var stepNumber = 0;
        //foreach (var step in plan.Steps)
        //{
        //    yield return $"<tr><td>{++stepNumber}</td><td>{step.Name}</td><td>{step.PluginName}</td><td>{step.Description}</td></tr>";
        //}
        //yield return "</table>";
        //yield return "<br/>Executing plan...<br/>";
        //var finalResult = "";
        //await foreach (var item in ExecuteSequentialPlannerByStep(chatRequestModel, kernel, plan, GetFinalResult).WithCancellation(cancellationToken))
        //{
        //    yield return item;
        //}

        //var result = finalResult;
        //Console.WriteLine($"Final Result from plan:\n{result}");
        //if (runAsChat)
        //{
        //    yield return "<h3>Execution Complete</h3><h4> Chat:</h4>";
        //    await foreach (var p in ExecuteChatStream(query, result, kernel).WithCancellation(cancellationToken))
        //        yield return p;
        //}
        //else
        //{
        //    yield return "<h3>Execution Complete</h3>";
        //}

        //yield break;

        //void GetFinalResult(string returnedResult) => finalResult = returnedResult;
    }

    //private static async IAsyncEnumerable<string> ExecuteSequentialPlannerByStep(ChatRequestModel chatRequestModel, Kernel kernel,
    //    Plan plan, Action<string> finalResultDelegate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    //{
    //    var stepNumber = 0;
    //    var finalResult = "";
    //    var context = kernel.CreateNewContext();
    //    foreach (var input in chatRequestModel.Variables ?? new Dictionary<string, string>())
    //    {
    //        context.Variables[input.Key] = input.Value;
    //    }

    //    while (plan.HasNextStep)
    //    {
    //        var hasError = false;
    //        yield return $"<h3>Step {++stepNumber}</h3>";
    //        string? stepResultString;
    //        try
    //        {
    //            var stepResult = await plan.InvokeNextStepAsync(context, cancellationToken);
    //            stepResultString = stepResult.State.ToString();
    //            finalResult = stepResult.State.ToString();
    //            Console.WriteLine($"{string.Join("\n", stepResult.State.Select(x => $"{x.Key} -- {x.Value}"))}");
    //        }
    //        catch (Exception ex)
    //        {
    //            stepResultString = $"Error on step {stepNumber}\n\n {ex.Message}\n\n{ex.StackTrace}";
    //            hasError = true;
    //        }

    //        yield return $"{stepResultString}\n\n";
    //        if (hasError) break;
    //    }

    //    finalResultDelegate(finalResult);
    //}

    //private async Task<(Plan plan, string skillStepsHeader, bool isError)> GeneratePlanAsync(string query,
    //    ISequentialPlanner planner)
    //{
    //    Plan plan;
    //    var skillStepsHeader = "<tr><th>Step</th> <th>Function</th><th> Skill</th><th> Description</th></tr>";
    //    var isError = false;
    //    try
    //    {
    //        plan = await planner.CreatePlanAsync(query);
    //        _loggerFactory.CreateLogger<CoreKernelService>()
    //            .LogInformation("Plan:\n{plan.ToPlanString()}", plan.ToPlanString());
    //        // Console.WriteLine($"Plan:\n{plan.ToPlanString()}");
    //    }
    //    catch (Exception ex)
    //    {
    //        plan = new Plan(query);
    //        skillStepsHeader = ex.ToString();
    //        isError = true;
    //    }

    //    return (plan, skillStepsHeader, isError);
    //}
    //private Task<(Plan plan, string skillStepsHeader, bool isError)> GeneratePlanAsync(string query,
    //    IStepwisePlanner planner)
    //{
    //    Plan plan;

    //    var isError = false;
    //    try
    //    {
    //        plan = planner.CreatePlan(query);
    //        _loggerFactory.CreateLogger<CoreKernelService>()
    //            .LogInformation("Plan:\n{plan.ToPlanString()}", plan.ToPlanString());
    //        //Console.WriteLine($"Plan:\n{plan.ToPlanString()}");
    //    }
    //    catch (Exception ex)
    //    {
    //        plan = new Plan(query);
    //        SkillStepsHeader = ex.ToString();
    //        isError = true;
    //    }

    //    return Task.FromResult((plan, SkillStepsHeader, isError));
    //}
    //private async Task<(HandlebarsPlan plan, string skillStepsHeader, bool isError)> GeneratePlanAsync(string query,
    //    HandlebarsPlanner planner)
    //{
    //    HandlebarsPlan plan;
    //    var skillStepsHeader = "<tr><th>Name</th> <th>Value</th></tr>";
    //    var isError = false;
    //    try
    //    {
    //        plan = await planner.CreatePlanAsync(query);
    //        _loggerFactory.CreateLogger<CoreKernelService>()
    //            .LogInformation("Plan:\n{plan.ToPlanString()}", plan.ToString());
    //        //Console.WriteLine($"Plan:\n{plan.ToPlanString()}");
    //    }
    //    catch (Exception ex)
    //    {
    //        var kernel = CreateKernel();
    //        plan = new HandlebarsPlan(kernel, query);
    //        skillStepsHeader = ex.ToString();
    //        isError = true;
    //    }
    //    return (plan, skillStepsHeader, isError);
    //}
    private async IAsyncEnumerable<string> ExecuteChatStream(string query, string result, Kernel kernel, bool withFunctionCall = false)
    {
        var systemPrmpt =
            $$$"""
                You are "Adventures in Semantic Kernal" a helpful, intelligent and resourceful AI chatbot. Respond to the user to the best of your ability using the following Context. If Context indicates an error. Aplogize for the error and explain the error as well as possible.
                The following [Context] is based on the user's query and with in a medium with access to real-time information and web access. Assume [Context] was generated with access to real-time information. Use the [Context] along with the query to respond. Respond with the exact language in [Context] when appropriate.
                NEVER REVEAL THAT ANY CONTEXT WAS PROVIDED.
                [Context]
                {{{result}}}
                """;
        Console.WriteLine($"InitialSysPrompt:\n{systemPrmpt}");
        var chatService = new OpenAIChatCompletionService(TestConfiguration.OpenAI.ModelId, TestConfiguration.OpenAI.ApiKey, loggerFactory: _loggerFactory);
        var engine = new KernelPromptTemplateFactory(_loggerFactory).Create(new PromptTemplateConfig(systemPrmpt));
        var ctx = new KernelArguments();
        var finalSysPrompt = await engine.RenderAsync(kernel, ctx);

        var chat = new ChatHistory(finalSysPrompt);
        foreach (var exchange in _chatExchanges)
        {
            chat.AddUserMessage(exchange.UserMessage);
            chat.AddAssistantMessage(exchange.AssistantMessage);
        }

        var settings = new OpenAIPromptExecutionSettings { Temperature = 0.7, TopP = 1.0, MaxTokens = 3000 };
        if (withFunctionCall)
            settings.ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions;
        chat.AddUserMessage(query);
        var response = "";
        await foreach (var token in chatService.GetStreamingChatMessageContentsAsync(chat, settings))
        {
            Console.WriteLine($"Response Json:\n {JsonSerializer.Serialize(token)}");
            response += token.Content;
            yield return token.Content;
        }

        var userMessage = $"User: {query}";
        var assistantMessage = $"Assistant: {response}";
        _chatExchanges.Add(new ChatExchange(userMessage, assistantMessage));
    }

    private async Task<Kernel> CreateKernelWithPlugins(IReadOnlyCollection<KernelPlugin> pluginFunctions, string model = "gpt-4-1106-preview")
    {
        var kernel = CreateKernel(model);
        kernel.Plugins.AddRange(pluginFunctions);
        return kernel;
    }


    #endregion
}