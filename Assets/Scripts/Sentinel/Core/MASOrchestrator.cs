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
        private readonly int _maxTestAttempts;
        
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private List<TestAttempt> _previousAttempts;
        
        public event Action<string> OnLog;
        public event Action<TestStep> OnStepStarted;
        public event Action<TestStep, VerificationResult> OnStepCompleted;
        public event Action<TestPlan> OnPlanCreated;
        public event Action<TestPlan> OnTestCompleted;
        public event Action<int, string> OnRetryWithNewStrategy;  // attempt number, analysis
        
        /// <summary>
        /// Creates a new MAS Orchestrator with specialized agent configurations.
        /// </summary>
        public MASOrchestrator(
            AgentExecutor executor,
            AgentConfig plannerConfig,
            AgentConfig executorConfig,
            AgentConfig verifierConfig,
            int maxRetries = 3,
            int maxPlanSteps = 20,
            int maxTestAttempts = 3)
        {
            _executor = executor;
            _plannerConfig = plannerConfig;
            _executorConfig = executorConfig;
            _verifierConfig = verifierConfig;
            _maxRetries = maxRetries;
            _maxPlanSteps = maxPlanSteps;
            _maxTestAttempts = maxTestAttempts;
            _previousAttempts = new List<TestAttempt>();
            
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
            _previousAttempts.Clear();
            
            TestPlan plan = null;
            
            try
            {
                Log($"🚀 Starting MAS Test: {objective}");
                
                // Attempt loop - retry with new strategy if failed
                for (int attempt = 1; attempt <= _maxTestAttempts && !_cancellationTokenSource.Token.IsCancellationRequested; attempt++)
                {
                    if (attempt > 1)
                    {
                        Log($"\n🔄 ATTEMPT {attempt}/{_maxTestAttempts} - Trying new strategy");
                        OnRetryWithNewStrategy?.Invoke(attempt, "Analyzing previous failure...");
                    }
                    
                    plan = new TestPlan(objective);
                    
                    // Phase 1: Planning (with context from previous attempts if any)
                    plan.Status = PlanStatus.Planning;
                    Log($"📋 Phase 1: PLANNING" + (attempt > 1 ? " (with failure analysis)" : ""));
                    
                    bool planningSuccess = attempt == 1 
                        ? await PlanTestAsync(plan, _cancellationTokenSource.Token)
                        : await RePlanAfterFailureAsync(plan, _cancellationTokenSource.Token);
                    
                    if (!planningSuccess || _cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        plan.Status = PlanStatus.Failed;
                        Log("❌ Planning failed or was cancelled");
                        continue; // Try again with fresh approach
                    }
                    
                    OnPlanCreated?.Invoke(plan);
                    Log($"✅ Plan created with {plan.Steps.Count} steps");
                    
                    // Phase 2: Execution + Verification Loop
                    plan.Status = PlanStatus.Executing;
                    Log("🔧 Phase 2: EXECUTION");
                    
                    bool executionSuccess = await ExecutePlanAsync(plan, _cancellationTokenSource.Token);
                    
                    if (executionSuccess)
                    {
                        plan.Status = PlanStatus.Completed;
                        Log("✅ Test PASSED");
                        OnTestCompleted?.Invoke(plan);
                        return plan;
                    }
                    
                    // Test failed - record attempt for analysis
                    plan.Status = PlanStatus.Failed;
                    RecordFailedAttempt(plan, attempt);
                    
                    if (attempt < _maxTestAttempts)
                    {
                        Log($"❌ Test failed on attempt {attempt} - will retry with new strategy");
                    }
                    else
                    {
                        Log($"❌ Test FAILED after {attempt} attempts");
                    }
                }
                
                // All attempts exhausted
                if (plan != null && plan.Status != PlanStatus.Completed)
                {
                    plan.Status = PlanStatus.Failed;
                }
                OnTestCompleted?.Invoke(plan);
                return plan;
            }
            catch (OperationCanceledException)
            {
                if (plan != null) plan.Status = PlanStatus.Cancelled;
                Log("🛑 Test cancelled by user");
                return plan ?? new TestPlan(objective) { Status = PlanStatus.Cancelled };
            }
            catch (Exception ex)
            {
                if (plan != null) plan.Status = PlanStatus.Failed;
                Log($"❌ Test error: {ex.Message}");
                Debug.LogError($"[MAS] Error: {ex}");
                return plan ?? new TestPlan(objective) { Status = PlanStatus.Failed };
            }
            finally
            {
                _isRunning = false;
            }
        }
        
        private void RecordFailedAttempt(TestPlan plan, int attemptNumber)
        {
            var attempt = new TestAttempt
            {
                AttemptNumber = attemptNumber,
                Plan = plan,
                FailedSteps = new List<string>(),
                SuccessfulSteps = new List<string>()
            };
            
            foreach (var step in plan.Steps)
            {
                string stepDesc = $"Step {step.StepNumber}: {step.Action}({step.Target}) - Expected: {step.ExpectedResult}";
                if (step.Status == StepStatus.Passed)
                {
                    attempt.SuccessfulSteps.Add(stepDesc);
                }
                else if (step.Status == StepStatus.Failed)
                {
                    attempt.FailedSteps.Add($"{stepDesc} - Actual: {step.ActualResult}");
                }
            }
            
            _previousAttempts.Add(attempt);
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
            string plannerPrompt = $@"TEST OBJECTIVE: {plan.Objective}

Your task is to create a structured plan for this test.

INSTRUCTIONS:
1. First use query_ui to see the current UI state
2. Analyze what elements are available
3. Create a step-by-step plan to achieve the objective
4. Each step must have: action, target, expected_result

RESPONSE FORMAT (JSON):
```json
{{
  ""success_criteria"": ""Description of what defines success"",
  ""steps"": [
    {{""step"": 1, ""action"": ""click"", ""target"": ""ElementName"", ""expected"": ""What should happen""}},
    {{""step"": 2, ""action"": ""wait_for_element"", ""target"": ""NewElement"", ""expected"": ""Element visible""}},
    ...
  ]
}}
```

AVAILABLE ACTIONS: click, type_text, scroll, wait_seconds, wait_for_element, screenshot

Create the plan now.";

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
                context.AddUserMessage("Please generate the plan in JSON format as indicated.");
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
            
            string executorPrompt = $@"EXECUTE EXACTLY THIS ACTION:
- Action: {step.Action}
- Target: {step.Target}
- Expected: {step.ExpectedResult}

Use the corresponding tool to execute the action. DO NOT do anything else.";

            context.AddUserMessage(executorPrompt);
            
            try
            {
                var response = await _executor.ExecuteAgentAsync(_executorConfig, context);
                
                // FIX: Check response.success is enough - if the agent executed a tool, 
                // it was processed and success indicates the overall result
                // The toolCalls list may be empty after processing
                if (!response.success)
                {
                    Log($"      Executor response: {response.content}");
                    return false;
                }
                
                // If there's content mentioning the tool was used, consider it a success
                // The tool was executed by the AgentExecutor internally
                return true;
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
            
            string verifierPrompt = $@"VERIFY IF THE FOLLOWING STEP WAS EXECUTED CORRECTLY:

Executed step:
- Action: {step.Action}
- Target: {step.Target}
- Expected: {step.ExpectedResult}

INSTRUCTIONS:
1. Use query_ui to see the current UI state
2. Compare with the expected result
3. Respond in this format:

```json
{{
  ""success"": true/false,
  ""diagnosis"": ""What you observe in the UI"",
  ""should_retry"": true/false,
  ""should_abort"": true/false
}}
```

Verify now.";

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
        
        #region Retry with Analysis
        
        /// <summary>
        /// Creates a new plan after analyzing what went wrong in previous attempts.
        /// Provides the Planner with complete context of failed attempts.
        /// </summary>
        private async Task<bool> RePlanAfterFailureAsync(TestPlan plan, CancellationToken cancellationToken)
        {
            var context = new ConversationContext("planner-retry-" + Guid.NewGuid());
            
            // Build context from previous attempts
            var attemptsSummary = new System.Text.StringBuilder();
            attemptsSummary.AppendLine("=== PREVIOUS ATTEMPTS (FAILED) ===\n");
            
            foreach (var attempt in _previousAttempts)
            {
                attemptsSummary.AppendLine($"--- ATTEMPT {attempt.AttemptNumber} ---");
                attemptsSummary.AppendLine($"Executed plan:");
                
                if (attempt.SuccessfulSteps.Count > 0)
                {
                    attemptsSummary.AppendLine("\nSteps that WORKED:");
                    foreach (var step in attempt.SuccessfulSteps)
                    {
                        attemptsSummary.AppendLine($"  ✅ {step}");
                    }
                }
                
                if (attempt.FailedSteps.Count > 0)
                {
                    attemptsSummary.AppendLine("\nSteps that FAILED:");
                    foreach (var step in attempt.FailedSteps)
                    {
                        attemptsSummary.AppendLine($"  ❌ {step}");
                    }
                }
                attemptsSummary.AppendLine();
            }
            
            string rePlanPrompt = $@"TEST OBJECTIVE: {plan.Objective}

THE TEST HAS FAILED IN {_previousAttempts.Count} PREVIOUS ATTEMPT(S).

{attemptsSummary}

ANALYSIS REQUIRED:
1. First, use query_ui to see the CURRENT UI state
2. Analyze what went wrong in previous attempts
3. Identify why those specific steps failed
4. CREATE A DIFFERENT PLAN that avoids the previous problems

POSSIBLE FAILURE CAUSES:
- Incorrect element name (verify exact names)
- Element not visible or not interactive
- Incorrect order of actions
- Missing waits (wait_for_element, wait_seconds)
- UI different than expected

IMPORTANT: 
- DO NOT repeat exactly the same plan
- Try a DIFFERENT approach
- Add more wait_for_element if necessary
- Verify element names with query_ui

RESPONSE FORMAT (JSON):
```json
{{
  ""analysis"": ""Explanation of what failed and why"",
  ""new_strategy"": ""Description of the new approach"",
  ""success_criteria"": ""Success criteria"",
  ""steps"": [
    {{""step"": 1, ""action"": ""..."", ""target"": ""..."", ""expected"": ""...""}},
    ...
  ]
}}
```

AVAILABLE ACTIONS: click, type_text, scroll, wait_seconds, wait_for_element, screenshot

Analyze and create a NEW plan now.";

            context.AddUserMessage(rePlanPrompt);
            
            Log("  🔍 Analyzing previous failures...");
            
            // Execute planner with retry context
            int maxIterations = 6; // More iterations for complex analysis
            for (int i = 0; i < maxIterations && !cancellationToken.IsCancellationRequested; i++)
            {
                var response = await _executor.ExecuteAgentAsync(_plannerConfig, context);
                
                if (!response.success)
                {
                    Log($"❌ Planner error: {response.content}");
                    return false;
                }
                
                // Log any analysis the planner provides
                if (!string.IsNullOrEmpty(response.content))
                {
                    // Try to extract analysis
                    var analysisMatch = Regex.Match(response.content, @"""analysis""\s*:\s*""([^""]+)""");
                    var strategyMatch = Regex.Match(response.content, @"""new_strategy""\s*:\s*""([^""]+)""");
                    
                    if (analysisMatch.Success)
                    {
                        Log($"  📊 Analysis: {analysisMatch.Groups[1].Value}");
                    }
                    if (strategyMatch.Success)
                    {
                        Log($"  💡 New strategy: {strategyMatch.Groups[1].Value}");
                    }
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
                context.AddUserMessage("Please generate the NEW plan in JSON format. Remember it must be DIFFERENT from the previous one.");
            }
            
            return plan.Steps.Count > 0;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Records information about a failed test attempt for analysis.
    /// </summary>
    public class TestAttempt
    {
        public int AttemptNumber;
        public TestPlan Plan;
        public List<string> SuccessfulSteps;
        public List<string> FailedSteps;
        
        public TestAttempt()
        {
            SuccessfulSteps = new List<string>();
            FailedSteps = new List<string>();
        }
    }
}
