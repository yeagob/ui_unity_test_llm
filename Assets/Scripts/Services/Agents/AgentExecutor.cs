using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatSystem.Models.LLM;
using ChatSystem.Models.Context;
using ChatSystem.Models.Agents;
using ChatSystem.Models.Tools;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Services.Tools.Interfaces;
using ChatSystem.Services.Agents.Interfaces;
using ChatSystem.Services.Logging;
using ChatSystem.Services.LLM;
using ChatSystem.Services.Tools;
using ChatSystem.Enums;

namespace ChatSystem.Services.Agents
{
    public class AgentExecutor : IAgentExecutor
    {
        private readonly Dictionary<string, IToolSet> registeredToolSets;
        private readonly Dictionary<string, AgentConfig> agentConfigs;
        
        public AgentExecutor()
        {
            registeredToolSets = new Dictionary<string, IToolSet>();
            agentConfigs = new Dictionary<string, AgentConfig>();
        }
        
        public async Task<AgentResponse> ExecuteAgentAsync(string agentId, ConversationContext context)
        {
            LoggingService.LogAgentExecution(agentId, "Context: " + context);
            
            if (!agentConfigs.TryGetValue(agentId, out AgentConfig agentConfig))
            {
                LoggingService.LogError($"Agent {agentId} not found");
                return CreateErrorResponse(agentId, "Agent configuration not found");
            }
            
            if (!agentConfig.enabled)
            {
                LoggingService.LogWarning($"Agent {agentId} is disabled");
                return CreateErrorResponse(agentId, "Agent is disabled");
            }
            
            try
            {
                LLMRequest request = BuildLLMRequest(agentConfig, context);
                LLMResponse llmResponse = await ExecuteLLMCallAsync(request, agentConfig);
                
                if (llmResponse.toolCalls != null && llmResponse.toolCalls.Count > 0)
                {
                    context.AddAssistantMessage(llmResponse.content, llmResponse.toolCalls);
                    
                    ToolDebugContext debugContext = CreateDebugContext(agentConfig, context);
                    
                    List<ToolResponse> toolResponses = await ExecuteToolCallsAsync(
                        llmResponse.toolCalls, agentConfig.maxToolCalls, debugContext);
                    
                    //Esto llega vacío si no sería redundante
                    foreach (ToolResponse toolResponse in toolResponses)
                    {
                        context.AddToolMessage(toolResponse.content, toolResponse.toolCallId);
                    }
                    
                    LLMRequest followUpRequest = BuildLLMRequest(agentConfig, context);
                    llmResponse = await ExecuteLLMCallAsync(followUpRequest, agentConfig);
                }
                
                LoggingService.LogAgentExecution(agentId, "Completed");
                
                return new AgentResponse
                {
                    agentId = agentId,
                    content = llmResponse.content,
                    toolCalls = llmResponse.toolCalls,
                    success = true,
                    timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Agent {agentId} execution failed: {ex.Message}");
                return CreateErrorResponse(agentId, ex.Message);
            }
        }
        
        /// <summary>
        /// Executes the agent using the provided AgentConfig directly (no registration needed).
        /// Useful for Editor tools where config is serialized but executor state is lost.
        /// </summary>
        public async Task<AgentResponse> ExecuteAgentAsync(AgentConfig agentConfig, ConversationContext context)
        {
            if (agentConfig == null)
            {
                LoggingService.LogError("AgentConfig is null");
                return CreateErrorResponse("unknown", "Agent configuration is null");
            }
            
            // Auto-register if not already registered
            if (!agentConfigs.ContainsKey(agentConfig.agentId))
            {
                RegisterAgent(agentConfig);
            }
            
            return await ExecuteAgentAsync(agentConfig.agentId, context);
        }
        
        public void RegisterAgent(AgentConfig agentConfig)
        {
            if (agentConfig == null || string.IsNullOrEmpty(agentConfig.agentId))
            {
                LoggingService.LogError("Invalid agent configuration");
                return;
            }
            
            agentConfigs[agentConfig.agentId] = agentConfig;
        }
        
        public void RegisterToolSet(IToolSet toolSet)
        {
            if (toolSet == null)
            {
                LoggingService.LogError("Cannot register null ToolSet");
                return;
            }
            
            string toolSetName = toolSet.GetType().Name;
            registeredToolSets[toolSetName] = toolSet;
            LoggingService.LogInfo($"ToolSet {toolSetName} registered.");
        }
        
        public void UnregisterToolSet(string toolSetName)
        {
            if (registeredToolSets.Remove(toolSetName))
            {
                LoggingService.LogInfo($"ToolSet {toolSetName} unregistered");
            }
        }
        
        public List<string> GetRegisteredToolSets()
        {
            return new List<string>(registeredToolSets.Keys);
        }
        
        private ToolDebugContext CreateDebugContext(AgentConfig agentConfig, ConversationContext context)
        {
            if (!agentConfig.debugTools)
                return ToolDebugContext.Disabled;
                
            ConversationToolDebugHandler debugHandler = new ConversationToolDebugHandler(context);
            return new ToolDebugContext(true, debugHandler);
        }
        
        private LLMRequest BuildLLMRequest(AgentConfig agentConfig, ConversationContext context)
        {
            List<Message> messages = new List<Message>();
            
            if (agentConfig.systemPrompt != null)
            {
                messages.Add(new Message
                {
                    role = MessageRole.System,
                    content = agentConfig.GetFullSystemPrompt(),
                    timestamp = DateTime.UtcNow
                });
            }
            
            messages.AddRange(context.GetAllMessages());
            
            List<ToolConfiguration> toolConfigs = new List<ToolConfiguration>();
            foreach (ToolConfig tool in agentConfig.availableTools)
            {
                if (tool != null && tool.enabled)
                {
                    toolConfigs.Add(new ToolConfiguration(tool));
                }
            }
            
            return new LLMRequest
            {
                messages = messages,
                tools = toolConfigs,
                maxTokens = agentConfig.maxResponseTokens,
                temperature = agentConfig.modelConfig?.temperature ?? 0.7f,
                model = agentConfig.modelConfig?.modelName ?? "default",
                provider = agentConfig.modelConfig?.provider ?? ServiceProvider.Custom
            };
        }
        
        private async Task<LLMResponse> ExecuteLLMCallAsync(LLMRequest request, AgentConfig agentConfig)
        {
            switch (request.provider)
            {
                case ServiceProvider.OpenAI:
                    return await OpenAIService.CompleteChatAsync(
                        request, 
                        agentConfig.token, 
                        agentConfig.serviceUrl);
                
                case ServiceProvider.QWEN:
                    return await QWENService.CompleteChatAsync(
                        request, 
                        agentConfig.token, 
                        agentConfig.serviceUrl);
                
                default:
                    LoggingService.LogWarning($"Unsupported provider: {request.provider}. Using fallback simulation.");
                    return await SimulateLLMCallAsync(request);
            }
        }
        
        private async Task<LLMResponse> SimulateLLMCallAsync(LLMRequest request)
        {
            await Task.Delay(1000);
            
            return new LLMResponse
            {
                content = "I understand your request and I'm here to help.",
                toolCalls = null,
                model = request.model,
                timestamp = DateTime.UtcNow,
                outputTokens = UnityEngine.Random.Range(50, 200),
                success = true
            };
        }
        
        private async Task<List<ToolResponse>> ExecuteToolCallsAsync(List<ToolCall> toolCalls, int maxCalls, ToolDebugContext debugContext)
        {
            List<ToolResponse> responses = new List<ToolResponse>();
            int callsToExecute = Math.Min(toolCalls.Count, maxCalls);
            
            for (int i = 0; i < callsToExecute; i++)
            {
                ToolCall call = toolCalls[i];
                
                try
                {
                    ToolResponse result = await ExecuteToolAsync(call.name, call.arguments, debugContext);
                    
                    responses.Add(new ToolResponse
                    {
                        toolCallId = call.id,
                        content = result.content,
                        success = true,
                        responseTimestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    responses.Add(new ToolResponse
                    {
                        toolCallId = call.id,
                        content = ex.Message,
                        success = false,
                        responseTimestamp = DateTime.UtcNow
                    });
                    
                    LoggingService.LogToolResponse(call.name, "Error: " + ex.Message);
                }
            }
            
            return responses;
        }
        
        private async Task<ToolResponse> ExecuteToolAsync(string toolName, Dictionary<string, object> arguments, ToolDebugContext debugContext)
        {
            foreach (IToolSet toolSet in registeredToolSets.Values)
            {
                if (toolSet.IsToolSupported(toolName))
                {
                    return await toolSet.ExecuteToolAsync(new ToolCall(toolName, arguments), debugContext);
                }
            }
            
            throw new InvalidOperationException($"Tool {toolName} not found in any registered ToolSet");
        }
        
        private AgentResponse CreateErrorResponse(string agentId, string error)
        {
            return new AgentResponse
            {
                agentId = agentId,
                content = $"Error: {error}",
                success = false,
                timestamp = DateTime.UtcNow
            };
        }
    }
}
