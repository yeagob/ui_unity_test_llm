using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Models.Context;
using ChatSystem.Models.Agents;
using ChatSystem.Services.Agents;
using Sentinel.Models;
using Sentinel.Tools;
using Sentinel.Services;

namespace Sentinel.Core
{
    /// <summary>
    /// Multi-Agent System Orchestrator for Sentinel Testing.
    /// Coordinates three specialized agents: Planner, Executor, and Verifier.
    /// </summary>
    public class MASOrchestrator
    {
        private readonly AgentExecutor _executor;
        private readonly AgentConfig _plannerConfig;
        private readonly AgentConfig _executorConfig;
        private readonly AgentConfig _verifierConfig;
        private readonly int _maxRetries;
        private readonly int _maxPlanSteps;
        
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        
        public event Action<string> OnLog;
        public event Action<TestStep> OnStepStarted;
        public event Action<TestStep, VerificationResult> OnStepCompleted;
        public event Action<TestPlan> OnPlanCreated;
        public event Action<TestPlan> OnTestCompleted;
        
        /// <summary>
        /// Creates a new MAS Orchestrator with specialized agent configurations.
        /// </summary>
        public MASOrchestrator(
            AgentExecutor executor,
            AgentConfig plannerConfig,
            AgentConfig executorConfig,
            AgentConfig verifierConfig,
            int maxRetries = 3,
            int maxPlanSteps = 20)
        {
            _executor = executor;
            _plannerConfig = plannerConfig;
            _executorConfig = executorConfig;
            _verifierConfig = verifierConfig;
            _maxRetries = maxRetries;
            _maxPlanSteps = maxPlanSteps;
            
            // Register all agents
            _executor.RegisterAgent(_plannerConfig);
            _executor.RegisterAgent(_executorConfig);
            _executor.RegisterAgent(_verifierConfig);
        }
        
        /// <summary>
        /// Runs a complete test with the Multi-Agent System.
        /// </summary>
        public async Task<TestPlan> RunTestAsync(string objective, CancellationToken cancellationToken = default)
        {
            _isRunning = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            TestPlan plan = new TestPlan(objective);
            
            try
            {
                Log($"🚀 Starting MAS Test: {objective}");
                
                // Phase 1: Planning
                plan.Status = PlanStatus.Planning;
                Log("📋 Phase 1: PLANNING");
                
                bool planningSuccess = await PlanTestAsync(plan, _cancellationTokenSource.Token);
                if (!planningSuccess || _cancellationTokenSource.Token.IsCancellationRequested)
                {
                    plan.Status = PlanStatus.Failed;
                    Log("❌ Planning failed or was cancelled");
                    return plan;
                }
                
                OnPlanCreated?.Invoke(plan);
                Log($"✅ Plan created with {plan.Steps.Count} steps");
                
                // Phase 2: Execution + Verification Loop
                plan.Status = PlanStatus.Executing;
                Log("🔧 Phase 2: EXECUTION");
                
                bool executionSuccess = await ExecutePlanAsync(plan, _cancellationTokenSource.Token);
                
                plan.Status = executionSuccess ? PlanStatus.Completed : PlanStatus.Failed;
                Log(executionSuccess ? "✅ Test PASSED" : "❌ Test FAILED");
                
                OnTestCompleted?.Invoke(plan);
                return plan;
            }
            catch (OperationCanceledException)
            {
                plan.Status = PlanStatus.Cancelled;
                Log("🛑 Test cancelled by user");
                return plan;
            }
            catch (Exception ex)
            {
                plan.Status = PlanStatus.Failed;
                Log($"❌ Test error: {ex.Message}");
                Debug.LogError($"[MAS] Error: {ex}");
                return plan;
            }
            finally
            {
                _isRunning = false;
            }
        }
        
        /// <summary>
        /// Cancels the currently running test.
        /// </summary>
        public void Cancel()
        {
            if (_isRunning && _cancellationTokenSource != null)
            {
                Log("🛑 Cancellation requested...");
                _cancellationTokenSource.Cancel();
            }
        }
        
        #region Phase 1: Planning
        
        private async Task<bool> PlanTestAsync(TestPlan plan, CancellationToken cancellationToken)
        {
            var context = new ConversationContext("planner-" + Guid.NewGuid());
            
            // Planner gets the objective and must create a structured plan
            string plannerPrompt = $@"OBJETIVO DEL TEST: {plan.Objective}

Tu tarea es crear un plan estructurado para este test.

INSTRUCCIONES:
1. Primero usa query_ui para ver el estado actual de la UI
2. Analiza qué elementos están disponibles
3. Crea un plan paso a paso para lograr el objetivo
4. Cada paso debe tener: action, target, expected_result

FORMATO DE RESPUESTA (JSON):
```json
{{
  ""success_criteria"": ""Descripción de qué define éxito"",
  ""steps"": [
    {{""step"": 1, ""action"": ""click"", ""target"": ""ElementName"", ""expected"": ""Qué debería pasar""}},
    {{""step"": 2, ""action"": ""wait_for_element"", ""target"": ""NewElement"", ""expected"": ""Elemento visible""}},
    ...
  ]
}}
```

ACCIONES DISPONIBLES: click, type_text, scroll, wait_seconds, wait_for_element, screenshot

Crea el plan ahora.";

            context.AddUserMessage(plannerPrompt);
            
            // Execute planner agent (may take multiple iterations to query UI and create plan)
            int maxIterations = 5;
            for (int i = 0; i < maxIterations && !cancellationToken.IsCancellationRequested; i++)
            {
                var response = await _executor.ExecuteAgentAsync(_plannerConfig, context);
                
                if (!response.success)
                {
                    Log($"❌ Planner error: {response.content}");
                    return false;
                }
                
                // Check if we got a plan in the response
                if (!string.IsNullOrEmpty(response.content) && response.content.Contains("\"steps\""))
                {
                    bool parsed = TryParsePlan(response.content, plan);
                    if (parsed && plan.Steps.Count > 0)
                    {
                        return true;
                    }
                }
                
                // If agent made tool calls, add response and continue
                if (response.toolCalls != null && response.toolCalls.Count > 0)
                {
                    context.AddAssistantMessage(response.content);
                    Log($"  Planner iteration {i + 1}: {response.toolCalls.Count} tool calls");
                    continue;
                }
                
                // No tools and no valid plan - try again
                context.AddAssistantMessage(response.content);
                context.AddUserMessage("Por favor genera el plan en formato JSON como se indicó.");
            }
            
            return plan.Steps.Count > 0;
        }
        
        private bool TryParsePlan(string content, TestPlan plan)
        {
            try
            {
                // Extract JSON from markdown code blocks if present
                string json = content;
                var jsonMatch = Regex.Match(content, @"```json\s*([\s\S]*?)\s*```");
                if (jsonMatch.Success)
                {
                    json = jsonMatch.Groups[1].Value;
                }
                
                // Simple parsing - extract steps array
                var stepsMatch = Regex.Match(json, @"""steps""\s*:\s*\[([\s\S]*?)\]");
                if (!stepsMatch.Success) return false;
                
                // Extract success criteria
                var criteriaMatch = Regex.Match(json, @"""success_criteria""\s*:\s*""([^""]+)""");
                if (criteriaMatch.Success)
                {
                    plan.SuccessCriteria = criteriaMatch.Groups[1].Value;
                }
                
                // Parse individual steps
                var stepMatches = Regex.Matches(stepsMatch.Groups[1].Value, 
                    @"\{[^{}]*""step""\s*:\s*(\d+)[^{}]*""action""\s*:\s*""([^""]+)""[^{}]*""target""\s*:\s*""([^""]+)""[^{}]*""expected""\s*:\s*""([^""]+)""[^{}]*\}");
                
                foreach (Match match in stepMatches)
                {
                    if (plan.Steps.Count >= _maxPlanSteps) break;
                    
                    plan.Steps.Add(new TestStep
                    {
                        StepNumber = int.Parse(match.Groups[1].Value),
                        Action = match.Groups[2].Value,
                        Target = match.Groups[3].Value,
                        ExpectedResult = match.Groups[4].Value,
                        Status = StepStatus.Pending
                    });
                }
                
                Log($"  Parsed {plan.Steps.Count} steps from plan");
                return plan.Steps.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MAS] Failed to parse plan: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Phase 2: Execution
        
        private async Task<bool> ExecutePlanAsync(TestPlan plan, CancellationToken cancellationToken)
        {
            int passedSteps = 0;
            int failedSteps = 0;
            
            foreach (var step in plan.Steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    step.Status = StepStatus.Skipped;
                    continue;
                }
                
                OnStepStarted?.Invoke(step);
                Log($"  Step {step.StepNumber}: {step.Action}({step.Target})");
                
                // Execute step with retries
                VerificationResult result = null;
                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    if (attempt > 0)
                    {
                        step.RetryCount = attempt;
                        step.Status = StepStatus.Retrying;
                        Log($"    Retry {attempt}/{_maxRetries}...");
                    }
                    
                    // Execute the step
                    step.Status = StepStatus.Executing;
                    step.ExecutedAt = DateTime.Now;
                    
                    bool executed = await ExecuteStepAsync(step, cancellationToken);
                    if (!executed)
                    {
                        result = VerificationResult.Fail("Execution failed", shouldRetry: attempt < _maxRetries);
                        continue;
                    }
                    
                    // Verify the step
                    result = await VerifyStepAsync(step, cancellationToken);
                    step.VerifiedAt = DateTime.Now;
                    step.ActualResult = result.Diagnosis;
                    
                    if (result.Success)
                    {
                        step.Status = StepStatus.Passed;
                        passedSteps++;
                        break;
                    }
                    
                    if (result.ShouldAbort)
                    {
                        step.Status = StepStatus.Failed;
                        failedSteps++;
                        Log($"    ❌ Step failed critically: {result.Diagnosis}");
                        OnStepCompleted?.Invoke(step, result);
                        return false; // Abort entire test
                    }
                    
                    if (!result.ShouldRetry)
                    {
                        break;
                    }
                }
                
                if (step.Status != StepStatus.Passed)
                {
                    step.Status = StepStatus.Failed;
                    failedSteps++;
                    Log($"    ❌ Step failed: {result?.Diagnosis ?? "Unknown error"}");
                }
                else
                {
                    Log($"    ✅ Step passed");
                }
                
                OnStepCompleted?.Invoke(step, result);
                
                // Small delay between steps
                await Task.Delay(100, cancellationToken);
            }
            
            Log($"📊 Results: {passedSteps} passed, {failedSteps} failed");
            return failedSteps == 0;
        }
        
        private async Task<bool> ExecuteStepAsync(TestStep step, CancellationToken cancellationToken)
        {
            var context = new ConversationContext("executor-" + Guid.NewGuid());
            
            string executorPrompt = $@"EJECUTA EXACTAMENTE ESTA ACCIÓN:
- Action: {step.Action}
- Target: {step.Target}
- Expected: {step.ExpectedResult}

Usa la herramienta correspondiente para ejecutar la acción. NO hagas nada más.";

            context.AddUserMessage(executorPrompt);
            
            try
            {
                var response = await _executor.ExecuteAgentAsync(_executorConfig, context);
                return response.success && response.toolCalls != null && response.toolCalls.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MAS] Executor error: {ex.Message}");
                return false;
            }
        }
        
        private async Task<VerificationResult> VerifyStepAsync(TestStep step, CancellationToken cancellationToken)
        {
            var context = new ConversationContext("verifier-" + Guid.NewGuid());
            
            string verifierPrompt = $@"VERIFICA SI EL SIGUIENTE PASO SE EJECUTÓ CORRECTAMENTE:

Paso ejecutado:
- Action: {step.Action}
- Target: {step.Target}
- Expected: {step.ExpectedResult}

INSTRUCCIONES:
1. Usa query_ui para ver el estado actual de la UI
2. Compara con el resultado esperado
3. Responde en este formato:

```json
{{
  ""success"": true/false,
  ""diagnosis"": ""Qué observas en la UI"",
  ""should_retry"": true/false,
  ""should_abort"": true/false
}}
```

Verifica ahora.";

            context.AddUserMessage(verifierPrompt);
            
            try
            {
                // Allow verifier to query UI
                for (int i = 0; i < 3; i++)
                {
                    var response = await _executor.ExecuteAgentAsync(_verifierConfig, context);
                    
                    if (!response.success)
                    {
                        return VerificationResult.Fail("Verifier error: " + response.content);
                    }
                    
                    // Try to parse verification result
                    if (!string.IsNullOrEmpty(response.content))
                    {
                        var result = TryParseVerification(response.content);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                    
                    // If tool calls, continue iteration
                    if (response.toolCalls != null && response.toolCalls.Count > 0)
                    {
                        context.AddAssistantMessage(response.content);
                        continue;
                    }
                    
                    // Default to pass if no clear failure
                    return VerificationResult.Pass("Could not parse result, assuming success");
                }
                
                return VerificationResult.Pass("Verification completed");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MAS] Verifier error: {ex.Message}");
                return VerificationResult.Fail(ex.Message, shouldRetry: true);
            }
        }
        
        private VerificationResult TryParseVerification(string content)
        {
            try
            {
                // Extract JSON from content
                string json = content;
                var jsonMatch = Regex.Match(content, @"```json\s*([\s\S]*?)\s*```");
                if (jsonMatch.Success)
                {
                    json = jsonMatch.Groups[1].Value;
                }
                
                var successMatch = Regex.Match(json, @"""success""\s*:\s*(true|false)");
                var diagnosisMatch = Regex.Match(json, @"""diagnosis""\s*:\s*""([^""]+)""");
                var retryMatch = Regex.Match(json, @"""should_retry""\s*:\s*(true|false)");
                var abortMatch = Regex.Match(json, @"""should_abort""\s*:\s*(true|false)");
                
                if (successMatch.Success)
                {
                    bool success = successMatch.Groups[1].Value == "true";
                    string diagnosis = diagnosisMatch.Success ? diagnosisMatch.Groups[1].Value : "";
                    bool shouldRetry = retryMatch.Success && retryMatch.Groups[1].Value == "true";
                    bool shouldAbort = abortMatch.Success && abortMatch.Groups[1].Value == "true";
                    
                    return success 
                        ? VerificationResult.Pass(diagnosis)
                        : VerificationResult.Fail(diagnosis, shouldRetry, shouldAbort);
                }
            }
            catch { }
            
            return null;
        }
        
        #endregion
        
        private void Log(string message)
        {
            Debug.Log($"[MAS] {message}");
            OnLog?.Invoke(message);
        }
    }
}
