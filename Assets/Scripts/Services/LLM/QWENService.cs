using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEngine;
using ChatSystem.Models.LLM;
using ChatSystem.Models.Context;
using ChatSystem.Models.Tools;
using ChatSystem.Services.Logging;
using ChatSystem.Enums;

namespace ChatSystem.Services.LLM
{
    public class QWENService
    {
        private const string DEFAULT_BASE_URL = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions";
        
        public static async Task<LLMResponse> CompleteChatAsync(LLMRequest request, string apiKey, string baseUrl = DEFAULT_BASE_URL)
        {
            try
            {
                LoggingService.LogInfo($"Making QWEN API call to model: {request.model}");
                
                string jsonPayload = BuildQWENPayload(request);
                
                UnityWebRequest webRequest = new UnityWebRequest(baseUrl, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                
                await SendWebRequestAsync(webRequest);
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;
                    LoggingService.LogInfo("QWEN API call successful");
                    return ParseQWENResponse(responseText, request.model);
                }
                else
                {
                    string error = $"QWEN API Error: {webRequest.error} - {webRequest.downloadHandler.text}";
                    LoggingService.LogError(error);
                    return CreateErrorResponse(request.model, error);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"QWEN API Exception: {ex.Message}");
                return CreateErrorResponse(request.model, ex.Message);
            }
        }
        
        private static string BuildQWENPayload(LLMRequest request)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{request.model}\",");
            sb.Append($"\"temperature\":{request.temperature}");
            
            sb.Append("\"messages\":[");
            for (int i = 0; i < request.messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                Message msg = request.messages[i];
                sb.Append("{");
                sb.Append($"\"role\":\"{GetQWENRole(msg.role)}\",");
                sb.Append($"\"content\":\"{EscapeJsonString(msg.content)}\"");
                sb.Append("}");
            }
            sb.Append("]");
            
            if (request.tools != null && request.tools.Count > 0)
            {
                sb.Append(",\"tools\":[");
                for (int i = 0; i < request.tools.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(request.tools[i].ToQWENFormat());
                }
                sb.Append("]");
                sb.Append(",\"tool_choice\":\"auto\"");
            }
            
            sb.Append("}");
            return sb.ToString();
        }
        
        private static LLMResponse ParseQWENResponse(string responseText, string model)
        {
            try
            {
                QWENResponseData response = ParseQWENJson(responseText);
                
                string content = string.Empty;
                List<ToolCall> toolCalls = null;
                int outputTokens = 0;
                
                if (response.choices != null && response.choices.Count > 0)
                {
                    QWENChoice choice = response.choices[0];
                    content = choice.message?.content ?? string.Empty;
                    
                    if (choice.message?.tool_calls != null && choice.message.tool_calls.Count > 0)
                    {
                        toolCalls = new List<ToolCall>();
                        foreach (QWENToolCall toolCall in choice.message.tool_calls)
                        {
                            Dictionary<string, object> args = SimpleJsonParser.ParseArguments(toolCall.function.arguments);
                            toolCalls.Add(new ToolCall(toolCall.function.name, args)
                            {
                                id = toolCall.id
                            });
                        }
                    }
                }
                
                if (response.usage != null)
                {
                    outputTokens = response.usage.completion_tokens;
                }
                
                return new LLMResponse
                {
                    content = content,
                    toolCalls = toolCalls,
                    model = model,
                    outputTokens = outputTokens,
                    success = true,
                    timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to parse QWEN response: {ex.Message}");
                return CreateErrorResponse(model, "Failed to parse API response");
            }
        }
        
        private static QWENResponseData ParseQWENJson(string json)
        {
            QWENResponseData response = new QWENResponseData();
            
            int choicesStart = json.IndexOf("\"choices\":[");
            if (choicesStart != -1)
            {
                response.choices = new List<QWENChoice>();
                int messageStart = json.IndexOf("\"message\":", choicesStart);
                if (messageStart != -1)
                {
                    QWENChoice choice = new QWENChoice();
                    choice.message = new QWENMessage();
                    
                    int contentStart = json.IndexOf("\"content\":\"", messageStart);
                    if (contentStart != -1)
                    {
                        contentStart += 11;
                        int contentEnd = json.IndexOf("\",", contentStart);
                        if (contentEnd == -1) contentEnd = json.IndexOf("\"}", contentStart);
                        if (contentEnd != -1)
                        {
                            choice.message.content = json.Substring(contentStart, contentEnd - contentStart);
                        }
                    }
                    
                    int toolCallsStart = json.IndexOf("\"tool_calls\":[", messageStart);
                    if (toolCallsStart != -1)
                    {
                        choice.message.tool_calls = ParseToolCalls(json, toolCallsStart);
                    }
                    
                    response.choices.Add(choice);
                }
            }
            
            int usageStart = json.IndexOf("\"usage\":{");
            if (usageStart != -1)
            {
                response.usage = new QWENUsage();
                int completionTokensStart = json.IndexOf("\"completion_tokens\":", usageStart);
                if (completionTokensStart != -1)
                {
                    completionTokensStart += 20;
                    int tokenEnd = json.IndexOf(",", completionTokensStart);
                    if (tokenEnd == -1) tokenEnd = json.IndexOf("}", completionTokensStart);
                    if (tokenEnd != -1)
                    {
                        string tokenStr = json.Substring(completionTokensStart, tokenEnd - completionTokensStart);
                        if (int.TryParse(tokenStr, out int tokens))
                        {
                            response.usage.completion_tokens = tokens;
                        }
                    }
                }
            }
            
            return response;
        }
        
        private static List<QWENToolCall> ParseToolCalls(string json, int startIndex)
        {
            List<QWENToolCall> toolCalls = new List<QWENToolCall>();
            
            int currentIndex = startIndex + 14;
            int bracketCount = 0;
            bool inToolCall = false;
            int toolCallStart = -1;
            
            for (int i = currentIndex; i < json.Length; i++)
            {
                char c = json[i];
                
                if (c == '{')
                {
                    if (!inToolCall)
                    {
                        inToolCall = true;
                        toolCallStart = i;
                    }
                    bracketCount++;
                }
                else if (c == '}')
                {
                    bracketCount--;
                    if (bracketCount == 0 && inToolCall)
                    {
                        string toolCallJson = json.Substring(toolCallStart, i - toolCallStart + 1);
                        QWENToolCall toolCall = ParseSingleToolCall(toolCallJson);
                        if (toolCall != null)
                        {
                            toolCalls.Add(toolCall);
                        }
                        inToolCall = false;
                    }
                }
                else if (c == ']' && bracketCount == 0)
                {
                    break;
                }
            }
            
            return toolCalls;
        }
        
        private static QWENToolCall ParseSingleToolCall(string json)
        {
            QWENToolCall toolCall = new QWENToolCall();
            toolCall.function = new QWENFunction();
            
            if (string.IsNullOrEmpty(json)) return toolCall;
            
            int idStart = json.IndexOf("\"id\":\"");
            if (idStart != -1)
            {
                idStart += 6;
                if (idStart < json.Length)
                {
                    int idEnd = json.IndexOf("\"", idStart);
                    if (idEnd > idStart)
                    {
                        toolCall.id = json.Substring(idStart, idEnd - idStart);
                    }
                }
            }
            
            int nameStart = json.IndexOf("\"name\":\"");
            if (nameStart != -1)
            {
                nameStart += 8;
                if (nameStart < json.Length)
                {
                    int nameEnd = json.IndexOf("\"", nameStart);
                    if (nameEnd > nameStart)
                    {
                        toolCall.function.name = json.Substring(nameStart, nameEnd - nameStart);
                    }
                }
            }
            
            int argsStart = json.IndexOf("\"arguments\":\"");
            if (argsStart != -1)
            {
                argsStart += 13;
                if (argsStart < json.Length)
                {
                    int argsEnd = json.LastIndexOf("\"");
                    if (argsEnd > argsStart)
                    {
                        toolCall.function.arguments = json.Substring(argsStart, argsEnd - argsStart);
                    }
                }
            }
            
            return toolCall;
        }
        
        private static string GetQWENRole(MessageRole role)
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
            if (string.IsNullOrEmpty(input)) return string.Empty;
            
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
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
    
    public class QWENResponseData
    {
        public List<QWENChoice> choices;
        public QWENUsage usage;
    }
    
    public class QWENChoice
    {
        public QWENMessage message;
    }
    
    public class QWENMessage
    {
        public string content;
        public List<QWENToolCall> tool_calls;
    }
    
    public class QWENToolCall
    {
        public string id;
        public QWENFunction function;
    }
    
    public class QWENFunction
    {
        public string name;
        public string arguments;
    }
    
    public class QWENUsage
    {
        public int completion_tokens;
    }
}
