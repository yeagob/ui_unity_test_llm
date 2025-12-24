using System;
using System.Threading.Tasks;
using UnityEngine;
using ChatSystem.Models.Context;
using ChatSystem.Models.Agents;
using ChatSystem.Services.Agents;
using Sentinel.Tools;
using Sentinel.Services;

namespace Sentinel.Core
{
    /// <summary>
    /// Agentic loop for Sentinel Testing.
    /// Executes the ReAct cycle: Perceive -> Reason -> Act -> Verify until goal is reached.
    /// </summary>
    public class SentinelAgentLoop
    {
        private readonly AgentExecutor _executor;
        private readonly string _agentId;
        private readonly int _maxIterations;
        
        private const string SYSTEM_PROMPT = @"Eres un agente de testing automatizado de UI. Tu objetivo es completar el test descrito por el usuario.

REGLAS ESTRICTAS:
1. SIEMPRE empieza con query_ui para entender el estado actual de la UI
2. Ejecuta UNA SOLA acción por turno (click, type_text, wait_seconds, etc.)
3. Después de cada acción, usa query_ui para verificar el resultado
4. Usa wait_for_element cuando esperes cambios de UI (transiciones, cargas)
5. Captura screenshots en momentos clave con labels descriptivos
6. Cuando el objetivo esté completo (o falle), llama finish_test con success=true/false

HERRAMIENTAS DISPONIBLES:
- query_ui: Obtiene la jerarquía de elementos UI visibles
- click(elementPath): Click en un elemento
- type_text(elementPath, text): Escribe texto en un campo
- scroll(elementPath, delta): Scroll en un elemento
- wait_seconds(seconds): Espera fija
- wait_for_element(elementPath, timeout): Espera hasta que un elemento aparezca
- check_element_state(elementPath): Verifica el estado de un elemento
- screenshot(label): Captura pantalla con etiqueta
- start_test(testName): Inicia el registro del test
- finish_test(success, summary): Finaliza y genera el informe

NUNCA asumas el estado de la UI - siempre verifica con query_ui.
NUNCA ejecutes múltiples acciones sin verificar entre ellas.";
        
        public SentinelAgentLoop(AgentExecutor executor, string agentId, int maxIterations = 20)
        {
            _executor = executor;
            _agentId = agentId;
            _maxIterations = maxIterations;
        }
        
        /// <summary>
        /// Runs an automated test with the given goal.
        /// </summary>
        public async Task<SentinelTestResult> RunTestAsync(string goal)
        {
            Debug.Log($"[Sentinel] Starting test: {goal}");
            
            ConversationContext context = new ConversationContext("sentinel-test-" + Guid.NewGuid());
            context.AddSystemMessage(SYSTEM_PROMPT);
            context.AddUserMessage($"OBJETIVO DEL TEST: {goal}\n\nComienza el test.");
            
            SentinelTestResult result = new SentinelTestResult
            {
                Goal = goal,
                StartTime = DateTime.Now
            };
            
            for (int iteration = 0; iteration < _maxIterations; iteration++)
            {
                Debug.Log($"[Sentinel] Iteration {iteration + 1}/{_maxIterations}");
                
                try
                {
                    AgentResponse response = await _executor.ExecuteAgentAsync(_agentId, context);
                    
                    if (!response.success)
                    {
                        Debug.LogError($"[Sentinel] Agent execution failed: {response.content}");
                        result.Success = false;
                        result.Summary = $"Agent error: {response.content}";
                        break;
                    }
                    
                    // Check if finish_test was called
                    if (response.toolCalls != null)
                    {
                        foreach (var toolCall in response.toolCalls)
                        {
                            if (toolCall.name == "finish_test")
                            {
                                result.Success = toolCall.arguments.TryGetValue("success", out object successVal) 
                                    && (successVal is bool b ? b : bool.Parse(successVal?.ToString() ?? "false"));
                                result.Summary = toolCall.arguments.TryGetValue("summary", out object summaryVal)
                                    ? summaryVal?.ToString() 
                                    : "Test completed";
                                result.Iterations = iteration + 1;
                                result.EndTime = DateTime.Now;
                                
                                Debug.Log($"[Sentinel] Test finished: {(result.Success ? "PASSED" : "FAILED")}");
                                return result;
                            }
                        }
                    }
                    
                    // Add assistant response to context for next iteration
                    if (!string.IsNullOrEmpty(response.content))
                    {
                        context.AddAssistantMessage(response.content);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Sentinel] Exception in iteration {iteration}: {ex.Message}");
                    result.Success = false;
                    result.Summary = $"Exception: {ex.Message}";
                    break;
                }
            }
            
            // Max iterations reached without finish_test
            result.Success = false;
            result.Summary = $"Max iterations ({_maxIterations}) reached without completion";
            result.Iterations = _maxIterations;
            result.EndTime = DateTime.Now;
            
            Debug.LogWarning("[Sentinel] Test timeout - max iterations reached");
            return result;
        }
    }
    
    /// <summary>
    /// Result of a Sentinel test execution.
    /// </summary>
    public class SentinelTestResult
    {
        public string Goal;
        public bool Success;
        public string Summary;
        public int Iterations;
        public DateTime StartTime;
        public DateTime EndTime;
        public TimeSpan Duration => EndTime - StartTime;
    }
}
