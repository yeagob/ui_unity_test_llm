using System;
using System.Collections.Generic;

namespace Sentinel.Models
{
    /// <summary>
    /// Represents a structured test plan created by the Planner Agent.
    /// </summary>
    [Serializable]
    public class TestPlan
    {
        public string TestName;
        public string Objective;
        public string SuccessCriteria;
        public List<TestStep> Steps;
        public DateTime CreatedAt;
        public PlanStatus Status;
        
        public TestPlan()
        {
            Steps = new List<TestStep>();
            CreatedAt = DateTime.Now;
            Status = PlanStatus.Created;
        }
        
        public TestPlan(string objective) : this()
        {
            Objective = objective;
            TestName = $"Test_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
    }
    
    /// <summary>
    /// Represents a single step in the test plan.
    /// </summary>
    [Serializable]
    public class TestStep
    {
        public int StepNumber;
        public string Action;
        public string Target;
        public string Parameters;
        public string ExpectedResult;
        public StepStatus Status;
        public string ActualResult;
        public int RetryCount;
        public DateTime? ExecutedAt;
        public DateTime? VerifiedAt;
        
        public TestStep()
        {
            Status = StepStatus.Pending;
            RetryCount = 0;
        }
        
        public TestStep(int stepNumber, string action, string target, string expected) : this()
        {
            StepNumber = stepNumber;
            Action = action;
            Target = target;
            ExpectedResult = expected;
        }
        
        public override string ToString()
        {
            return $"Step {StepNumber}: {Action}({Target}) - Expected: {ExpectedResult} - Status: {Status}";
        }
    }
    
    /// <summary>
    /// Status of the overall test plan.
    /// </summary>
    public enum PlanStatus
    {
        Created,
        Planning,
        Executing,
        Completed,
        Failed,
        Cancelled
    }
    
    /// <summary>
    /// Status of an individual test step.
    /// </summary>
    public enum StepStatus
    {
        Pending,
        Executing,
        Passed,
        Failed,
        Skipped,
        Retrying
    }
    
    /// <summary>
    /// Result of a verification check.
    /// </summary>
    [Serializable]
    public class VerificationResult
    {
        public bool Success;
        public string Diagnosis;
        public string Recommendation;
        public bool ShouldRetry;
        public bool ShouldAbort;
        public string ScreenshotPath;
        
        public static VerificationResult Pass(string diagnosis = "Step executed successfully")
        {
            return new VerificationResult
            {
                Success = true,
                Diagnosis = diagnosis,
                ShouldRetry = false,
                ShouldAbort = false
            };
        }
        
        public static VerificationResult Fail(string diagnosis, bool shouldRetry = true, bool shouldAbort = false)
        {
            return new VerificationResult
            {
                Success = false,
                Diagnosis = diagnosis,
                ShouldRetry = shouldRetry,
                ShouldAbort = shouldAbort,
                Recommendation = shouldRetry ? "Retry the step" : (shouldAbort ? "Abort the test" : "Continue to next step")
            };
        }
    }
}
