using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChatSystem.Models.Tools;
using ChatSystem.Enums;
using ChatSystem.Services.Tools.Interfaces;
using ChatSystem.Services.Logging;
using Sentinel.Interfaces;
using Sentinel.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sentinel.Tools
{
    /// <summary>
    /// MCP ToolSet for Sentinel Testing Agent.
    /// Provides tools for UI inspection, interaction, and reporting.
    /// </summary>
    public class SentinelToolSet : IToolSet
    {
        public string ToolSetId => "sentinel-testing-toolset";
        public ToolType ToolSetType => ToolType.Custom;
        
        private readonly IUIInspector _inspector;
        private readonly IUIInteractor _interactor;
        private readonly ITestReporter _reporter;
        
        public SentinelToolSet(Func<VisualElement> rootProvider, string reportDirectory = "Assets/TestReports")
        {
            _inspector = new UIInspectorService(rootProvider);
            _interactor = new UIInteractorService(rootProvider);
            _reporter = new TestReportService(reportDirectory);
        }
        
        public SentinelToolSet(IUIInspector inspector, IUIInteractor interactor, ITestReporter reporter)
        {
            _inspector = inspector;
            _interactor = interactor;
            _reporter = reporter;
        }
        
        public async Task<ToolResponse> ExecuteToolAsync(ToolCall toolCall)
        {
            return await ExecuteToolAsync(toolCall, ToolDebugContext.Disabled);
        }
        
        public async Task<ToolResponse> ExecuteToolAsync(ToolCall toolCall, ToolDebugContext debugContext)
        {
            LoggingService.LogToolCall(toolCall.name, toolCall.arguments);
            
            try
            {
                ToolResponse response = toolCall.name switch
                {
                    "query_ui" => await ExecuteQueryUIAsync(toolCall),
                    "click" => await ExecuteClickAsync(toolCall),
                    "type_text" => await ExecuteTypeAsync(toolCall),
                    "scroll" => await ExecuteScrollAsync(toolCall),
                    "wait_seconds" => await ExecuteWaitSecondsAsync(toolCall),
                    "wait_for_element" => await ExecuteWaitForElementAsync(toolCall),
                    "check_element_state" => await ExecuteCheckElementStateAsync(toolCall),
                    "screenshot" => await ExecuteScreenshotAsync(toolCall),
                    "start_test" => await ExecuteStartTestAsync(toolCall),
                    "finish_test" => await ExecuteFinishTestAsync(toolCall),
                    _ => CreateErrorResponse(toolCall.id, $"Unknown tool: {toolCall.name}")
                };
                
                LoggingService.LogToolResponse(toolCall.name, response.content);
                
                if (response.success)
                {
                    debugContext.LogToolExecution(toolCall.name, ToolSetId, SerializeArguments(toolCall.arguments), response.content);
                }
                else
                {
                    debugContext.LogToolError(toolCall.name, ToolSetId, response.content);
                }
                
                return response;
            }
            catch (Exception ex)
            {
                debugContext.LogToolError(toolCall.name, ToolSetId, ex.Message);
                return CreateErrorResponse(toolCall.id, $"Tool execution failed: {ex.Message}");
            }
        }
        
        public async Task<bool> ValidateToolCallAsync(ToolCall toolCall)
        {
            await Task.CompletedTask;
            if (!IsToolSupported(toolCall.name)) return false;
            return true;
        }
        
        public bool IsToolSupported(string toolName)
        {
            return toolName switch
            {
                "query_ui" or "click" or "type_text" or "scroll" 
                or "wait_seconds" or "wait_for_element" or "check_element_state"
                or "screenshot" or "start_test" or "finish_test" => true,
                _ => false
            };
        }
        
        #region Tool Implementations
        
        private async Task<ToolResponse> ExecuteQueryUIAsync(ToolCall toolCall)
        {
            await Task.Delay(10);
            string hierarchy = _inspector.GetUIHierarchy();
            _reporter.LogStep("query_ui", "Retrieved UI hierarchy");
            return CreateSuccessResponse(toolCall.id, hierarchy);
        }
        
        private async Task<ToolResponse> ExecuteClickAsync(ToolCall toolCall)
        {
            string elementPath = GetString(toolCall.arguments, "elementPath");
            bool success = await _interactor.ClickAsync(elementPath);
            _reporter.LogStep($"click({elementPath})", success ? "OK" : "FAILED");
            
            return success 
                ? CreateSuccessResponse(toolCall.id, $"Clicked element: {elementPath}")
                : CreateErrorResponse(toolCall.id, $"Failed to click: {elementPath}");
        }
        
        private async Task<ToolResponse> ExecuteTypeAsync(ToolCall toolCall)
        {
            string elementPath = GetString(toolCall.arguments, "elementPath");
            string text = GetString(toolCall.arguments, "text");
            bool success = await _interactor.TypeAsync(elementPath, text);
            _reporter.LogStep($"type_text({elementPath}, '{text}')", success ? "OK" : "FAILED");
            
            return success
                ? CreateSuccessResponse(toolCall.id, $"Typed '{text}' into: {elementPath}")
                : CreateErrorResponse(toolCall.id, $"Failed to type into: {elementPath}");
        }
        
        private async Task<ToolResponse> ExecuteScrollAsync(ToolCall toolCall)
        {
            string elementPath = GetString(toolCall.arguments, "elementPath");
            float delta = GetFloat(toolCall.arguments, "delta", 100f);
            bool success = await _interactor.ScrollAsync(elementPath, delta);
            _reporter.LogStep($"scroll({elementPath}, {delta})", success ? "OK" : "FAILED");
            
            return success
                ? CreateSuccessResponse(toolCall.id, $"Scrolled {elementPath} by {delta}")
                : CreateErrorResponse(toolCall.id, $"Failed to scroll: {elementPath}");
        }
        
        private async Task<ToolResponse> ExecuteWaitSecondsAsync(ToolCall toolCall)
        {
            float seconds = GetFloat(toolCall.arguments, "seconds", 1f);
            await _interactor.WaitSecondsAsync(seconds);
            _reporter.LogStep($"wait_seconds({seconds})", "OK");
            return CreateSuccessResponse(toolCall.id, $"Waited {seconds} seconds");
        }
        
        private async Task<ToolResponse> ExecuteWaitForElementAsync(ToolCall toolCall)
        {
            string elementPath = GetString(toolCall.arguments, "elementPath");
            float timeout = GetFloat(toolCall.arguments, "timeout", 5f);
            bool found = await _interactor.WaitForElementAsync(elementPath, timeout);
            _reporter.LogStep($"wait_for_element({elementPath}, {timeout}s)", found ? "FOUND" : "TIMEOUT");
            
            return found
                ? CreateSuccessResponse(toolCall.id, $"Element found: {elementPath}")
                : CreateErrorResponse(toolCall.id, $"Timeout waiting for: {elementPath}");
        }
        
        private async Task<ToolResponse> ExecuteCheckElementStateAsync(ToolCall toolCall)
        {
            await Task.Delay(10);
            string elementPath = GetString(toolCall.arguments, "elementPath");
            string state = _inspector.GetElementState(elementPath);
            _reporter.LogStep($"check_element_state({elementPath})", "Retrieved");
            return CreateSuccessResponse(toolCall.id, state);
        }
        
        private async Task<ToolResponse> ExecuteScreenshotAsync(ToolCall toolCall)
        {
            await Task.Delay(100); // Small delay to ensure UI is rendered
            string label = GetString(toolCall.arguments, "label", "screenshot");
            string path = _reporter.CaptureScreenshot(label);
            
            return path != null
                ? CreateSuccessResponse(toolCall.id, $"Screenshot saved: {path}")
                : CreateErrorResponse(toolCall.id, "Failed to capture screenshot");
        }
        
        private async Task<ToolResponse> ExecuteStartTestAsync(ToolCall toolCall)
        {
            await Task.Delay(10);
            string testName = GetString(toolCall.arguments, "testName", "Unnamed Test");
            _reporter.StartTest(testName);
            return CreateSuccessResponse(toolCall.id, $"Test started: {testName}");
        }
        
        private async Task<ToolResponse> ExecuteFinishTestAsync(ToolCall toolCall)
        {
            await Task.Delay(10);
            bool success = GetBool(toolCall.arguments, "success", false);
            string summary = GetString(toolCall.arguments, "summary", "Test completed");
            string reportPath = _reporter.FinishTest(success, summary);
            
            return reportPath != null
                ? CreateSuccessResponse(toolCall.id, $"Test finished. Report: {reportPath}")
                : CreateErrorResponse(toolCall.id, "Failed to generate report");
        }
        
        #endregion
        
        #region Helpers
        
        private string GetString(Dictionary<string, object> args, string key, string defaultValue = "")
        {
            if (args != null && args.TryGetValue(key, out object value))
            {
                return value?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }
        
        private float GetFloat(Dictionary<string, object> args, string key, float defaultValue = 0f)
        {
            if (args != null && args.TryGetValue(key, out object value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out float parsed)) return parsed;
            }
            return defaultValue;
        }
        
        private bool GetBool(Dictionary<string, object> args, string key, bool defaultValue = false)
        {
            if (args != null && args.TryGetValue(key, out object value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out bool parsed)) return parsed;
            }
            return defaultValue;
        }
        
        private string SerializeArguments(Dictionary<string, object> arguments)
        {
            if (arguments == null || arguments.Count == 0) return "{}";
            List<string> parts = new List<string>();
            foreach (var kvp in arguments)
            {
                parts.Add($"{kvp.Key}:{kvp.Value}");
            }
            return "{" + string.Join(", ", parts) + "}";
        }
        
        private ToolResponse CreateSuccessResponse(string toolCallId, string content)
        {
            return new ToolResponse
            {
                toolCallId = toolCallId,
                content = content,
                success = true,
                responseTimestamp = DateTime.UtcNow
            };
        }
        
        private ToolResponse CreateErrorResponse(string toolCallId, string error)
        {
            return new ToolResponse
            {
                toolCallId = toolCallId,
                content = error,
                success = false,
                responseTimestamp = DateTime.UtcNow
            };
        }
        
        #endregion
    }
}
