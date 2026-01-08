using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sentinel.Configuration
{
    /// <summary>
    /// ScriptableObject that defines a Test Run - a collection of tests to execute sequentially.
    /// Generates a combined report after all tests complete.
    /// </summary>
    [CreateAssetMenu(fileName = "TestRun", menuName = "Sentinel/Test Run", order = 1)]
    public class TestRunConfiguration : ScriptableObject
    {
        [Header("Run Information")]
        [Tooltip("Name of this test run")]
        public string runName;
        
        [TextArea(2, 4)]
        [Tooltip("Description of what this run tests")]
        public string description;
        
        [Tooltip("Category/tag for organizing runs")]
        public string category;
        
        [Header("Tests")]
        [Tooltip("List of tests to execute in order")]
        public List<TestConfiguration> tests = new List<TestConfiguration>();
        
        [Header("Run Settings")]
        [Tooltip("Continue running remaining tests if one fails")]
        public bool continueOnFailure = true;
        
        [Tooltip("Delay in seconds between tests")]
        [Range(0, 10)]
        public float delayBetweenTests = 1f;
        
        [Tooltip("Generate individual screenshots for each test")]
        public bool screenshotPerTest = true;
        
        [Header("Report Settings")]
        [Tooltip("Directory for generated reports (relative to Assets)")]
        public string reportDirectory = "TestReports";
        
        [Tooltip("Include detailed logs in report")]
        public bool includeDetailedLogs = true;
        
        [Header("Last Run Results")]
        [HideInInspector]
        public DateTime lastRunTime;
        [HideInInspector]
        public int lastRunPassed;
        [HideInInspector]
        public int lastRunFailed;
        [HideInInspector]
        public int lastRunSkipped;
        [HideInInspector]
        public float lastRunDurationSeconds;
        [HideInInspector]
        public string lastReportPath;
        
        /// <summary>
        /// Returns true if this run has valid tests configured.
        /// </summary>
        public bool IsValid => tests != null && tests.Count > 0;
        
        /// <summary>
        /// Returns the number of valid (configured) tests.
        /// </summary>
        public int ValidTestCount
        {
            get
            {
                int count = 0;
                if (tests != null)
                {
                    foreach (var test in tests)
                    {
                        if (test != null && test.IsValid) count++;
                    }
                }
                return count;
            }
        }
        
        /// <summary>
        /// Updates the last run results.
        /// </summary>
        public void RecordRunResults(int passed, int failed, int skipped, float durationSeconds, string reportPath)
        {
            lastRunTime = DateTime.Now;
            lastRunPassed = passed;
            lastRunFailed = failed;
            lastRunSkipped = skipped;
            lastRunDurationSeconds = durationSeconds;
            lastReportPath = reportPath;
            
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        
        /// <summary>
        /// Returns a summary string of the last run.
        /// </summary>
        public string GetLastRunSummary()
        {
            if (lastRunTime == DateTime.MinValue)
                return "Never run";
            
            int total = lastRunPassed + lastRunFailed + lastRunSkipped;
            string status = lastRunFailed == 0 ? "✅ PASSED" : "❌ FAILED";
            return $"{status} - {lastRunPassed}/{total} passed ({lastRunDurationSeconds:F1}s)";
        }
    }
    
    /// <summary>
    /// Result of a single test within a run.
    /// </summary>
    [Serializable]
    public class TestRunResult
    {
        public string TestName;
        public string Objective;
        public bool Passed;
        public string Summary;
        public int StepsPassed;
        public int StepsFailed;
        public float DurationSeconds;
        public List<string> LogEntries;
        public DateTime StartTime;
        public DateTime EndTime;
        
        public TestRunResult(string testName)
        {
            TestName = testName;
            LogEntries = new List<string>();
            StartTime = DateTime.Now;
        }
        
        public void Complete(bool passed, string summary, int stepsPassed, int stepsFailed)
        {
            Passed = passed;
            Summary = summary;
            StepsPassed = stepsPassed;
            StepsFailed = stepsFailed;
            EndTime = DateTime.Now;
            DurationSeconds = (float)(EndTime - StartTime).TotalSeconds;
        }
    }
}
