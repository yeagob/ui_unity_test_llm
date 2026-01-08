using System;
using System.Collections.Generic;
using UnityEngine;
using ChatSystem.Configuration.ScriptableObjects;

namespace Sentinel.Configuration
{
    /// <summary>
    /// ScriptableObject for persisting and reusing test configurations.
    /// Can be saved as an asset and reloaded across sessions.
    /// </summary>
    [CreateAssetMenu(fileName = "TestConfig", menuName = "Sentinel/Test Configuration", order = 0)]
    public class TestConfiguration : ScriptableObject
    {
        [Header("Test Information")]
        [Tooltip("Name of this test configuration")]
        public string testName;
        
        [TextArea(3, 5)]
        [Tooltip("The objective/goal of this test")]
        public string objective;
        
        [TextArea(2, 4)]
        [Tooltip("What defines success for this test")]
        public string successCriteria;
        
        [Header("Agent Configuration")]
        [Tooltip("Agent for creating test plans")]
        public AgentConfig plannerAgent;
        
        [Tooltip("Agent for executing actions")]
        public AgentConfig executorAgent;
        
        [Tooltip("Agent for verifying results")]
        public AgentConfig verifierAgent;
        
        [Header("Test Settings")]
        [Range(1, 50)]
        [Tooltip("Maximum number of steps in the plan")]
        public int maxPlanSteps = 20;
        
        [Range(1, 10)]
        [Tooltip("Maximum retry attempts per step")]
        public int maxRetries = 3;
        
        [Tooltip("Take screenshot after each step")]
        public bool screenshotOnEachStep = false;
        
        [Header("Predefined Steps (Optional)")]
        [Tooltip("If defined, these steps will be used instead of generating a plan")]
        public List<TestStepConfig> predefinedSteps = new List<TestStepConfig>();
        
        [Header("Last Run")]
        [HideInInspector]
        public DateTime lastRunTime;
        [HideInInspector]
        public string lastRunResult;
        [HideInInspector]
        public int lastRunPassedSteps;
        [HideInInspector]
        public int lastRunFailedSteps;
        
        /// <summary>
        /// Returns true if this configuration has predefined steps.
        /// </summary>
        public bool HasPredefinedSteps => predefinedSteps != null && predefinedSteps.Count > 0;
        
        /// <summary>
        /// Returns true if all required agents are configured.
        /// </summary>
        public bool IsValid => plannerAgent != null && executorAgent != null && verifierAgent != null 
                               && !string.IsNullOrEmpty(objective);
        
        /// <summary>
        /// Updates the last run information.
        /// </summary>
        public void RecordRun(bool success, int passed, int failed)
        {
            lastRunTime = DateTime.Now;
            lastRunResult = success ? "PASSED" : "FAILED";
            lastRunPassedSteps = passed;
            lastRunFailedSteps = failed;
            
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
    
    /// <summary>
    /// Configuration for a single test step.
    /// </summary>
    [Serializable]
    public class TestStepConfig
    {
        [Tooltip("Action to perform: click, type_text, scroll, wait_seconds, wait_for_element, screenshot")]
        public string action;
        
        [Tooltip("Target element path or name")]
        public string target;
        
        [Tooltip("Additional parameters (e.g., text to type, scroll delta, wait duration)")]
        public string parameters;
        
        [Tooltip("Expected result after this step")]
        public string expectedResult;
        
        public TestStepConfig() { }
        
        public TestStepConfig(string action, string target, string expected)
        {
            this.action = action;
            this.target = target;
            this.expectedResult = expected;
        }
    }
}
