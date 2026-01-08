using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEngine;
using ChatSystem.Models.LLM;
using ChatSystem.Models.Context;
using ChatSystem.Models.Tools;
using ChatSystem.Models.LLM.OpenAI;
using ChatSystem.Services.Logging;
using ChatSystem.Enums;
using ChatSystem.Configuration.Voice;
using ChatSystem.Models.Communication;

namespace ChatSystem.Services.LLM
{
    public class OpenAIService
    {
        public static async Task<LLMResponse> CompleteChatAsync(LLMRequest request, string apiKey, string baseUrl)
        {
            try
            {
                string jsonPayload = BuildOpenAIPayload(request);
                
                LoggingService.LogDebug($"[OpenAIService] Making OpenAI API call to model- {request.model} with PAYLOAD: {jsonPayload}");
                
                UnityWebRequest webRequest = CreateWebRequest(jsonPayload, apiKey, baseUrl);
                
                await SendWebRequestAsync(webRequest);
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;
                    return ParseOpenAIResponse(responseText, request.model);
                }
                
                string error = $"[OpenAIService] OpenAI API Error: {webRequest.error} - {webRequest.downloadHandler.text}";
                
                LoggingService.LogError(error);
                
                return CreateErrorResponse(request.model, error);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[OpenAIService] OpenAI API Exception: {ex.Message}");
                return CreateErrorResponse(request.model, ex.Message);
            }
        }

        public static WebSocketEvent CreateRealtimeSessionUpdate(VoiceAgentConfig agentConfig, List<ToolConfiguration> tools)
        {
            return new WebSocketEvent
            {
                type = "session.update",
                eventId = Guid.NewGuid().ToString(),
                data = BuildRealtimeSessionData(agentConfig, tools),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        public static WebSocketEvent CreateRealtimeToolResponse(string toolCallId, string result)
        {
            return new WebSocketEvent
            {
                type = "conversation.item.create",
                eventId = Guid.NewGuid().ToString(),
                data = new
                {
                    item = new
                    {
                        type = "function_call_output",
                        call_id = toolCallId,
                        output = result
                    }
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        public static WebSocketEvent CreateRealtimeTextMessage(string message)
        {
            return new WebSocketEvent
            {
                type = "conversation.item.create",
                eventId = Guid.NewGuid().ToString(),
                data = new
                {
                    item = new
                    {
                        type = "message",
                        role = "user",
                        content = new[] { new { type = "input_text", text = message } }
                    }
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        public static WebSocketEvent CreateRealtimeAudioEvent(byte[] audioData)
        {
            string base64Audio = Convert.ToBase64String(audioData);
            return new WebSocketEvent
            {
                type = "input_audio_buffer.append",
                eventId = Guid.NewGuid().ToString(),
                data = new { audio = base64Audio },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static object BuildRealtimeSessionData(VoiceAgentConfig agentConfig, List<ToolConfiguration> tools)
        {
            return new
            {
                session = new
                {
                    model = agentConfig.Model,
                    voice = agentConfig.Voice,
                    instructions = GetSystemPromptFromConfig(agentConfig),
                    turn_detection = agentConfig.EnableTurnDetection ? 
                        new { type = "server_vad", threshold = 0.5, prefix_padding_ms = 300, silence_duration_ms = 200 } : null,
                    tools = BuildRealtimeToolsArray(tools),
                    tool_choice = "auto",
                    temperature = agentConfig.modelConfig?.temperature ?? 1.0f,
                    max_response_output_tokens = agentConfig.maxResponseTokens,
                    input_audio_format = ConvertToOpenAIFormat(agentConfig.VoiceSettings.inputFormat),
                    output_audio_format = ConvertToOpenAIFormat(agentConfig.VoiceSettings.outputFormat)
                }
            };
        }

        private static object[] BuildRealtimeToolsArray(List<ToolConfiguration> tools)
        {
            if (tools == null || tools.Count == 0)
                return new object[0];

            List<object> realtimeTools = new List<object>();
            
            foreach (ToolConfiguration tool in tools)
            {
                realtimeTools.Add(new
                {
                    type = "function",
                    name = tool.toolId,
                    description = tool.description,
                    parameters = BuildRealtimeParameters(tool)
                });
            }

            return realtimeTools.ToArray();
        }

        private static object BuildRealtimeParameters(ToolConfiguration tool)
        {
            if (tool._inputSchema?.properties == null || tool._inputSchema.properties.Count == 0)
            {
                return new { type = "object", properties = new { }, required = new string[0] };
            }

            Dictionary<string, object> properties = new Dictionary<string, object>();
            
            foreach (var property in tool._inputSchema.properties)
            {
                properties[property.Key] = new
                {
                    type = property.Value.type,
                    description = property.Value.description,
                    @enum = property.Value
                };
            }

            return new
            {
                type = "object",
                properties = properties,
                required = tool._inputSchema.required ?? new List<string>()
            };
        }

        private static string GetSystemPromptFromConfig(VoiceAgentConfig agentConfig)
        {
            return agentConfig?.systemPrompt?.content ?? 
                   "You are a helpful voice assistant with tool capabilities.";
        }

        private static string ConvertToOpenAIFormat(ChatSystem.Enums.AudioFormat format)
        {
            switch (format)
            {
                case ChatSystem.Enums.AudioFormat.PCM16:
                    return "pcm16";
                case ChatSystem.Enums.AudioFormat.PCM24:
                    return "pcm24";
                case ChatSystem.Enums.AudioFormat.G711_ULAW:
                    return "g711_ulaw";
                case ChatSystem.Enums.AudioFormat.G711_ALAW:
                    return "g711_alaw";
                default:
                    return "pcm16";
            }
        }
        
        private static UnityWebRequest CreateWebRequest(string jsonPayload, string apiKey, string baseUrl)
        {
            UnityWebRequest webRequest = new UnityWebRequest(baseUrl, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            
            return webRequest;
        }
        
        private static string BuildOpenAIPayload(LLMRequest request)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("{");
            stringBuilder.Append($"\"model\":\"{request.model}\",");
            stringBuilder.Append($"\"temperature\":{request.temperature.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");
            
            // Add reasoning support ONLY for compatible models (o1, o3, o4-mini family)
            // Models like gpt-4.x do NOT support reasoning_effort parameter
            if (request.enableThinking && request.reasoningEffort != ReasoningEffort.None && SupportsReasoningEffort(request.model))
            {
                string effortValue = GetReasoningEffortString(request.reasoningEffort);
                stringBuilder.Append($",\"reasoning_effort\":\"{effortValue}\"");
                LoggingService.LogInfo($"[OpenAIService] Thinking enabled with reasoning_effort: {effortValue}");
            }
            
            List<Message> filteredMessages = FilterMessagesForOpenAI(request.messages);
            AppendMessages(stringBuilder, filteredMessages);
            AppendTools(stringBuilder, request.tools);
            stringBuilder.Append("}");
            
            return stringBuilder.ToString();
        }
        
        /// <summary>
        /// Checks if the model supports the reasoning_effort parameter.
        /// Only OpenAI "o" family models (o1, o3, o4-mini, etc.) support this.
        /// </summary>
        private static bool SupportsReasoningEffort(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            
            string lower = modelName.ToLowerInvariant();
            
            // o1, o3, o4-mini family support reasoning_effort
            // Pattern: starts with "o" followed by a digit (o1, o3, o4, o1-preview, o1-mini, etc.)
            if (lower.StartsWith("o") && lower.Length > 1 && char.IsDigit(lower[1]))
            {
                return true;
            }
            
            return false;
        }
        
        private static string GetReasoningEffortString(ReasoningEffort effort)
        {
            switch (effort)
            {
                case ReasoningEffort.Low: return "low";
                case ReasoningEffort.Medium: return "medium";
                case ReasoningEffort.High: return "high";
                default: return "medium";
            }
        }
        
        private static List<Message> FilterMessagesForOpenAI(List<Message> messages)
        {
            List<Message> filtered = new List<Message>();
            
            for (int i = 0; i < messages.Count; i++)
            {
                Message msg = messages[i];
                
                if (msg.role == MessageRole.System && IsToolDebugMessage(msg.content))
                {
                    continue;
                }
                
                filtered.Add(msg);
            }
            
            return filtered;
        }
        
        private static bool IsToolDebugMessage(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }
                
            return content.StartsWith("🔧 Tool Executed:") || content.StartsWith("❌ Tool Error:");
        }
        
        private static void AppendMessages(StringBuilder sb, List<Message> messages)
        {
            sb.Append(",\"messages\":[");
            
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                Message msg = messages[i];
                sb.Append("{");
                sb.Append($"\"role\":\"{GetOpenAIRole(msg.role)}\",");
                sb.Append($"\"content\":\"{EscapeJsonString(msg.content)}\"");
                
                if (msg.role == MessageRole.Assistant && msg.toolCalls != null && msg.toolCalls.Count > 0)
                {
                    AppendFunctionToolCalls(sb, msg.toolCalls);
                }
                
                if (msg.role == MessageRole.Tool)
                {
                    sb.Append($",\"tool_call_id\":\"{msg.toolCallId}\"");
                }
                
                sb.Append("}");
            }
            sb.Append("]");
        }
        
        private static void AppendFunctionToolCalls(StringBuilder sb, List<ToolCall> toolCalls)
        {
            sb.Append(",\"tool_calls\":[");
            
            for (int i = 0; i < toolCalls.Count; i++)
            {
                if (i > 0) sb.Append(",");
                ToolCall toolCall = toolCalls[i];
                sb.Append("{");
                sb.Append($"\"id\":\"{toolCall.id}\",");
                sb.Append("\"type\":\"function\",");
                sb.Append("\"function\":{");
                sb.Append($"\"name\":\"{toolCall.name}\",");
                sb.Append($"\"arguments\":\"{EscapeJsonString(SerializeArguments(toolCall.arguments))}\"");
                sb.Append("}}");
            }
            
            sb.Append("]");
        }
        
        private static string SerializeArguments(Dictionary<string, object> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }
                
            StringBuilder sb = new StringBuilder();
            
            sb.Append("{");
            
            bool first = true;
            
            foreach (KeyValuePair<string, object> kvp in arguments)
            {
                if (!first)
                {
                    sb.Append(",");
                }
                
                sb.Append($"\"{kvp.Key}\":");
                
                if (kvp.Value is string)
                {
                    sb.Append($"\"{EscapeJsonString(kvp.Value.ToString())}\"");
                }
                else if (kvp.Value is bool)
                {
                    sb.Append(kvp.Value.ToString().ToLower());
                }
                else if (kvp.Value is int || kvp.Value is float || kvp.Value is double)
                {
                    sb.Append(kvp.Value);
                }
                else
                {
                    sb.Append($"\"{EscapeJsonString(kvp.Value?.ToString() ?? "")}\"");
                }
                
                first = false;
            }
            
            sb.Append("}");
            
            return sb.ToString();
        }
        
        private static void AppendTools(StringBuilder sb, List<ToolConfiguration> tools)
        {
            if (tools != null && tools.Count > 0)
            {
                sb.Append(",\"tools\":[");
                
                for (int i = 0; i < tools.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(tools[i].ToOpenAIFormat());
                }
                
                sb.Append("]");
                sb.Append(",\"tool_choice\":\"auto\"");
            }
        }
        
        private static LLMResponse ParseOpenAIResponse(string responseText, string model)
        {
            try
            {
                LoggingService.LogDebug($"OpenAI response: {responseText}");

                OpenAIResponse response = JsonUtility.FromJson<OpenAIResponse>(responseText);
                
                string content = ExtractContent(response);
                List<ToolCall> toolCalls = ExtractToolCalls(response);
                int outputTokens = ExtractTokenCount(response);
                
                bool success = HasValidResponse(content, toolCalls);
                
                return CreateSuccessResponse(content, toolCalls, model, outputTokens, success);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to parse OpenAI response: {ex.Message}");
                return CreateErrorResponse(model, "Failed to parse API response");
            }
        }
        
        private static string ExtractContent(OpenAIResponse response)
        {
            if (response.choices != null && response.choices.Count > 0)
            {
                string messageContent = response.choices[0].message.content;
                if (!string.IsNullOrWhiteSpace(messageContent) && messageContent != "null")
                {
                    return UnescapeJsonString(messageContent.Trim());
                }
            }
            return string.Empty;
        }
        
        private static List<ToolCall> ExtractToolCalls(OpenAIResponse response)
        {
            if (response.choices == null || response.choices.Count == 0)
            {
                return null;
            }
            
            List<OpenAIToolCall> apiToolCalls = response.choices[0].message.tool_calls;
            if (apiToolCalls == null || apiToolCalls.Count == 0)
            {
                return null;
            }
            
            List<ToolCall> toolCalls = new List<ToolCall>();
            
            foreach (OpenAIToolCall apiToolCall in apiToolCalls)
            {
                try
                {
                    Dictionary<string, object> args = SimpleJsonParser.ParseArguments(apiToolCall.function.arguments);
                    
                    toolCalls.Add(new ToolCall(apiToolCall.function.name, args)
                    {
                        id = apiToolCall.id
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Failed to parse tool call arguments: {ex.Message}");
                }
            }
            return toolCalls;
        }
        
        private static int ExtractTokenCount(OpenAIResponse response)
        {
            return response.usage.completion_tokens;
        }
        
        private static bool HasValidResponse(string content, List<ToolCall> toolCalls)
        {
            return !string.IsNullOrWhiteSpace(content) || (toolCalls != null && toolCalls.Count > 0);
        }
        
        private static LLMResponse CreateSuccessResponse(string content, List<ToolCall> toolCalls, string model, int outputTokens, bool success)
        {
            return new LLMResponse
            {
                content = content,
                toolCalls = toolCalls,
                model = model,
                outputTokens = outputTokens,
                success = success,
                timestamp = DateTime.UtcNow
            };
        }
        
        private static string GetOpenAIRole(MessageRole role)
        {
            switch (role)
            {
                case MessageRole.User: return "user";
                case MessageRole.Assistant: return "assistant";
                case MessageRole.System: return "system";
                case MessageRole.Tool: return "tool";
                
                default: return "user";
            }
        }
        
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
        
        private static string UnescapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            
            return input
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }
        
        private static LLMResponse CreateErrorResponse(string model, string error)
        {
            return new LLMResponse
            {
                content = $"Error: {error}",
                model = model,
                success = false,
                timestamp = DateTime.UtcNow
            };
        }
        
        private static async Task SendWebRequestAsync(UnityWebRequest request)
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            
            while (!operation.isDone)
            {
                await Task.Yield();
            }
        }
    }
}