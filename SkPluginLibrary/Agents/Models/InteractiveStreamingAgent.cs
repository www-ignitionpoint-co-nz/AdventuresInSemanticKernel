﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SkPluginLibrary.Agents.Extensions;
using SkPluginLibrary.Agents.Models.Events;

namespace SkPluginLibrary.Agents.Models
{
    public class InteractiveStreamingAgent : InteractiveAgentBase
    {
        public InteractiveStreamingAgent(AgentProxy agent, Kernel kernel, bool engageuser = false) : base(agent, kernel)
        {
            Plugins = agent.Plugins;
            if (engageuser)
                AddInteractivePlugin();
        }
        public string Id { get; } = Guid.NewGuid().ToString();
        public override event AgentStreamingResponseEventHandler? AgentStreamingResponse;
        public List<KernelPlugin> Plugins { get; set; }
        public void AddInteractivePlugin()
        {
            var function = KernelFunctionFactory.CreateFromMethod(typeof(InteractiveAgent).GetMethod("GetHumanInputAsync")!, this, "AskUser", "Ask user for information, or request clarification from user.");
            var plugin = KernelPluginFactory.CreateFromFunctions("InteractiveAgentPlugin", "", [function]);
            Plugins.Add(plugin);
        }
        public override async Task<AgentMessage?> RunAgentAsync(List<AgentMessage> chatHistory, PromptExecutionSettings? settings = null, CancellationToken cancellationToken = default)
        {
            if (Agent.Plugins.Count > Kernel.Plugins.Count)
            {
                Console.WriteLine("Adding Kernel Plugins");
                Kernel.Plugins.Clear();
                Kernel.Plugins.AddRange(Agent.Plugins);
            }
            var chat = Kernel.Services.GetRequiredService<IChatCompletionService>();
            var chatmessageHistory = chatHistory.AsChatHistory();
            chatmessageHistory.AddSystemMessage(SystemPrompt);
            var callBehavior = Plugins.Count > 0 ? ToolCallBehavior.AutoInvokeKernelFunctions : null;
            Console.WriteLine($"{Name} ToolCallBehavior: {callBehavior}");
            settings ??= new OpenAIPromptExecutionSettings() { Temperature = Temperature, ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, ChatSystemPrompt = SystemPrompt };
            AgentMessage? message = null;
            AgentMessageStreamUpdate? finalMessage = null;
            
            var hasStarted = false;
            try
            {
                await foreach (var update in chat.GetStreamingChatMessageContentsAsync(chatmessageHistory, settings, Kernel, cancellationToken))
                {
                    //var update2 = (OpenAIStreamingChatMessageContent)update;
                    //var toolCall = update2.ToolCallUpdate as StreamingFunctionToolCallUpdate;
                    //Console.WriteLine($"Tool call: {toolCall?.Name}, {toolCall?.ToolCallIndex}");
                    if (message is null)
                    {
                        message = new AgentMessage(update.Role.GetValueOrDefault(), update.Content, Name);
                    }
                    else
                    {
                        message.Content += update.Content;
                    }
                    var agentChatMessageContent = new AgentMessageStreamUpdate(update.Role, update.Content, Name, update.InnerContent, update.ChoiceIndex, update.ModelId, update.Encoding, update.Metadata);
                    finalMessage = agentChatMessageContent;
                    this.AgentStreamingResponse?.Invoke(this, new AgentStreamingResponseArgs(agentChatMessageContent) { IsStartToken = !hasStarted});
                    hasStarted = true;
                }
                if (finalMessage is not null)
                {
                    finalMessage.Content = "";
                    AgentStreamingResponse?.Invoke(this, new AgentStreamingResponseArgs(finalMessage) { IsCompleted = true });
                }
            }
            catch (OperationCanceledException canceledException)
            {
                Console.WriteLine(canceledException);
                return null;
            }
            return message;
        }
    }
}
