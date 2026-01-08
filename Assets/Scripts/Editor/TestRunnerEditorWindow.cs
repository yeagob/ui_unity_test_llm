using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Services.Agents;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Tools;
using Sentinel.Configuration;

namespace Sentinel.Editor
{
    /// <summary>
    /// Editor Window for running Test Run configurations - multiple tests sequentially.
    /// Generates comprehensive Markdown reports.
    /// </summary>
    public class TestRunnerEditorWindow : EditorWindow
    {
        [SerializeField] private TestRunConfiguration m_RunConfig;
        
        private ListView m_TestListView;
        private VisualElement m_LogContainer;
        private ScrollView m_LogScroll;
        private Label m_StatusLabel;
        private ProgressBar m_ProgressBar;
        private Button m_RunButton;
        private Button m_StopButton;
        
        private AgentExecutor m_Executor;
        private MASOrchestrator m_Orchestrator;
        private bool m_IsRunning;
        private bool m_StopRequested;
        
        private List<TestRunResult> m_Results = new List<TestRunResult>();
        private List<string> m_GlobalLogEntries = new List<string>();
        private DateTime m_RunStartTime;
        private int m_CurrentTestIndex;
        
        [MenuItem("Window/LLM/Test Runner")]
        public static void ShowWindow()
        {
            TestRunnerEditorWindow wnd = GetWindow<TestRunnerEditorWindow>();
            wnd.titleContent = new GUIContent("🏃 Test Runner");
            wnd.minSize = new Vector2(550, 650);
        }
        
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.backgroundColor = new Color(0.12f, 0.12f, 0.18f);
            
            // Header
            Label title = new Label("🏃 Test Runner");
            title.style.fontSize = 22;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.9f, 0.7f, 0.3f);
            title.style.marginBottom = 15;
            root.Add(title);
            
            // Test Run Configuration selector
            VisualElement configRow = new VisualElement();
            configRow.style.flexDirection = FlexDirection.Row;
            configRow.style.marginBottom = 15;
            
            ObjectField runConfigField = new ObjectField("📁 Test Run");
            runConfigField.objectType = typeof(TestRunConfiguration);
            runConfigField.style.flexGrow = 1;
            runConfigField.value = m_RunConfig;
            runConfigField.RegisterValueChangedCallback(evt => {
                m_RunConfig = evt.newValue as TestRunConfiguration;
                RefreshTestList();
            });
            configRow.Add(runConfigField);
            root.Add(configRow);
            
            // Run info box
            VisualElement infoBox = new VisualElement();
            infoBox.name = "info-box";
            infoBox.style.backgroundColor = new Color(0.15f, 0.15f, 0.22f);
            infoBox.style.paddingTop = 10;
            infoBox.style.paddingBottom = 10;
            infoBox.style.paddingLeft = 10;
            infoBox.style.paddingRight = 10;
            infoBox.style.borderTopLeftRadius = 5;
            infoBox.style.borderTopRightRadius = 5;
            infoBox.style.borderBottomLeftRadius = 5;
            infoBox.style.borderBottomRightRadius = 5;
            infoBox.style.marginBottom = 15;
            
            Label infoLabel = new Label("Select a Test Run configuration to see tests");
            infoLabel.name = "info-label";
            infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoBox.Add(infoLabel);
            root.Add(infoBox);
            
            // Tests list
            Label testsLabel = new Label("📋 Tests in Run");
            testsLabel.style.fontSize = 14;
            testsLabel.style.color = Color.white;
            testsLabel.style.marginBottom = 5;
            root.Add(testsLabel);
            
            m_TestListView = new ListView();
            m_TestListView.style.height = 150;
            m_TestListView.style.backgroundColor = new Color(0.1f, 0.1f, 0.14f);
            m_TestListView.style.borderTopLeftRadius = 5;
            m_TestListView.style.borderTopRightRadius = 5;
            m_TestListView.style.borderBottomLeftRadius = 5;
            m_TestListView.style.borderBottomRightRadius = 5;
            m_TestListView.style.marginBottom = 15;
            root.Add(m_TestListView);
            
            // Control buttons
            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginBottom = 15;
            
            m_RunButton = new Button(OnRunClicked);
            m_RunButton.text = "▶️ Run All Tests";
            m_RunButton.style.flexGrow = 1;
            m_RunButton.style.height = 40;
            m_RunButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.7f);
            m_RunButton.style.fontSize = 14;
            buttonRow.Add(m_RunButton);
            
            m_StopButton = new Button(OnStopClicked);
            m_StopButton.text = "⏹️ Stop";
            m_StopButton.style.width = 80;
            m_StopButton.style.height = 40;
            m_StopButton.style.marginLeft = 10;
            m_StopButton.style.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
            m_StopButton.style.display = DisplayStyle.None;
            buttonRow.Add(m_StopButton);
            
            Button openReportBtn = new Button(OnOpenLastReportClicked);
            openReportBtn.text = "📄 Last Report";
            openReportBtn.style.width = 100;
            openReportBtn.style.height = 40;
            openReportBtn.style.marginLeft = 10;
            buttonRow.Add(openReportBtn);
            
            root.Add(buttonRow);
            
            // Progress
            m_StatusLabel = new Label("Ready");
            m_StatusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            m_StatusLabel.style.marginBottom = 5;
            root.Add(m_StatusLabel);
            
            m_ProgressBar = new ProgressBar();
            m_ProgressBar.title = "Progress";
            m_ProgressBar.style.height = 22;
            m_ProgressBar.style.marginBottom = 15;
            root.Add(m_ProgressBar);
            
            // Log output
            Label logLabel = new Label("📜 Execution Log");
            logLabel.style.fontSize = 14;
            logLabel.style.color = Color.white;
            logLabel.style.marginBottom = 5;
            root.Add(logLabel);
            
            m_LogScroll = new ScrollView();
            m_LogScroll.style.flexGrow = 1;
            m_LogScroll.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            m_LogScroll.style.borderTopLeftRadius = 5;
            m_LogScroll.style.borderTopRightRadius = 5;
            m_LogScroll.style.borderBottomLeftRadius = 5;
            m_LogScroll.style.borderBottomRightRadius = 5;
            
            m_LogContainer = new VisualElement();
            m_LogContainer.style.paddingTop = 5;
            m_LogContainer.style.paddingBottom = 5;
            m_LogContainer.style.paddingLeft = 10;
            m_LogContainer.style.paddingRight = 10;
            m_LogScroll.Add(m_LogContainer);
            root.Add(m_LogScroll);
            
            // Initialize
            InitializeServices();
            RefreshTestList();
        }
        
        private void InitializeServices()
        {
            m_Executor = new AgentExecutor();
            m_Executor.RegisterToolSet(new SentinelToolSet(GetCurrentUIRoot));
            AddLog("🟢 Test Runner initialized", Color.green);
        }
        
        private VisualElement GetCurrentUIRoot()
        {
            var uiDocument = UnityEngine.Object.FindObjectOfType<UIDocument>();
            return uiDocument?.rootVisualElement;
        }
        
        private void RefreshTestList()
        {
            var infoLabel = rootVisualElement.Q<Label>("info-label");
            
            if (m_RunConfig == null)
            {
                if (infoLabel != null)
                    infoLabel.text = "Select a Test Run configuration to see tests";
                m_TestListView.itemsSource = null;
                m_TestListView.Rebuild();
                return;
            }
            
            // Update info
            if (infoLabel != null)
            {
                string info = $"📁 {m_RunConfig.runName}\n";
                info += $"📝 {m_RunConfig.description}\n";
                info += $"📊 {m_RunConfig.ValidTestCount}/{m_RunConfig.tests.Count} valid tests\n";
                info += $"🕐 Last run: {m_RunConfig.GetLastRunSummary()}";
                infoLabel.text = info;
            }
            
            // Setup list view
            m_TestListView.makeItem = () => {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop = 5;
                row.style.paddingBottom = 5;
                row.style.paddingLeft = 10;
                
                var status = new Label("⏳");
                status.name = "status";
                status.style.width = 25;
                row.Add(status);
                
                var name = new Label();
                name.name = "name";
                name.style.flexGrow = 1;
                name.style.color = Color.white;
                row.Add(name);
                
                var objective = new Label();
                objective.name = "objective";
                objective.style.color = new Color(0.6f, 0.6f, 0.6f);
                objective.style.fontSize = 10;
                objective.style.width = 200;
                objective.style.overflow = Overflow.Hidden;
                row.Add(objective);
                
                return row;
            };
            
            m_TestListView.bindItem = (element, index) => {
                var test = m_RunConfig.tests[index];
                var status = element.Q<Label>("status");
                var name = element.Q<Label>("name");
                var objective = element.Q<Label>("objective");
                
                if (test == null)
                {
                    status.text = "⚠️";
                    name.text = "(null)";
                    objective.text = "";
                }
                else
                {
                    status.text = test.IsValid ? "✓" : "⚠️";
                    name.text = test.testName ?? test.name;
                    objective.text = test.objective?.Length > 40 
                        ? test.objective.Substring(0, 40) + "..." 
                        : test.objective;
                }
            };
            
            m_TestListView.itemsSource = m_RunConfig.tests;
            m_TestListView.Rebuild();
        }
        
        private async void OnRunClicked()
        {
            if (m_IsRunning) return;
            
            if (m_RunConfig == null || m_RunConfig.ValidTestCount == 0)
            {
                AddLog("❌ No valid tests configured in this run", Color.red);
                return;
            }
            
            m_IsRunning = true;
            m_StopRequested = false;
            m_RunButton.style.display = DisplayStyle.None;
            m_StopButton.style.display = DisplayStyle.Flex;
            m_LogContainer.Clear();
            m_GlobalLogEntries.Clear();
            m_Results.Clear();
            m_RunStartTime = DateTime.Now;
            m_CurrentTestIndex = 0;
            
            int totalTests = m_RunConfig.ValidTestCount;
            m_ProgressBar.highValue = totalTests;
            m_ProgressBar.value = 0;
            
            AddLog($"🏃 Starting Test Run: {m_RunConfig.runName}", new Color(0.9f, 0.7f, 0.3f));
            AddLog($"📋 {totalTests} tests to execute", Color.white);
            AddLog("─────────────────────────────────", Color.gray);
            
            int passed = 0, failed = 0, skipped = 0;
            
            try
            {
                for (int i = 0; i < m_RunConfig.tests.Count && !m_StopRequested; i++)
                {
                    var testConfig = m_RunConfig.tests[i];
                    
                    if (testConfig == null || !testConfig.IsValid)
                    {
                        AddLog($"⏭️ Skipping invalid test at index {i}", Color.yellow);
                        skipped++;
                        continue;
                    }
                    
                    m_CurrentTestIndex = i;
                    m_StatusLabel.text = $"Running: {testConfig.testName} ({i + 1}/{m_RunConfig.tests.Count})";
                    
                    AddLog($"\n📌 Test {i + 1}: {testConfig.testName}", new Color(0.5f, 0.8f, 1f));
                    AddLog($"   Objective: {testConfig.objective}", Color.gray);
                    
                    var result = await RunSingleTestAsync(testConfig);
                    m_Results.Add(result);
                    
                    if (result.Passed)
                    {
                        passed++;
                        AddLog($"   ✅ PASSED ({result.StepsPassed} steps, {result.DurationSeconds:F1}s)", Color.green);
                    }
                    else
                    {
                        failed++;
                        AddLog($"   ❌ FAILED: {result.Summary}", Color.red);
                        
                        if (!m_RunConfig.continueOnFailure)
                        {
                            AddLog("🛑 Stopping run (continueOnFailure = false)", Color.yellow);
                            break;
                        }
                    }
                    
                    m_ProgressBar.value = passed + failed;
                    
                    // Delay between tests
                    if (i < m_RunConfig.tests.Count - 1 && m_RunConfig.delayBetweenTests > 0)
                    {
                        await Task.Delay((int)(m_RunConfig.delayBetweenTests * 1000));
                    }
                }
                
                if (m_StopRequested)
                {
                    skipped = m_RunConfig.ValidTestCount - passed - failed;
                    AddLog($"\n🛑 Run stopped by user", Color.yellow);
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Run error: {ex.Message}", Color.red);
            }
            
            // Generate report
            AddLog("\n─────────────────────────────────", Color.gray);
            float totalDuration = (float)(DateTime.Now - m_RunStartTime).TotalSeconds;
            
            string reportPath = GenerateReport(passed, failed, skipped, totalDuration);
            m_RunConfig.RecordRunResults(passed, failed, skipped, totalDuration, reportPath);
            
            // Final summary
            string finalStatus = failed == 0 ? "✅ ALL TESTS PASSED" : "❌ SOME TESTS FAILED";
            AddLog($"\n📊 {finalStatus}", failed == 0 ? Color.green : Color.red);
            AddLog($"   Passed: {passed}, Failed: {failed}, Skipped: {skipped}", Color.white);
            AddLog($"   Duration: {totalDuration:F1}s", Color.white);
            AddLog($"   Report: {reportPath}", Color.cyan);
            
            m_StatusLabel.text = $"{finalStatus} - Report saved";
            m_StatusLabel.style.color = failed == 0 ? Color.green : Color.red;
            
            m_IsRunning = false;
            m_RunButton.style.display = DisplayStyle.Flex;
            m_StopButton.style.display = DisplayStyle.None;
            
            RefreshTestList();
        }
        
        private async Task<TestRunResult> RunSingleTestAsync(TestConfiguration testConfig)
        {
            var result = new TestRunResult(testConfig.testName);
            result.Objective = testConfig.objective;
            
            try
            {
                // Create orchestrator for this test
                m_Orchestrator = new MASOrchestrator(
                    m_Executor,
                    testConfig.plannerAgent,
                    testConfig.executorAgent,
                    testConfig.verifierAgent,
                    testConfig.maxRetries,
                    testConfig.maxPlanSteps
                );
                
                // Capture logs for this test
                m_Orchestrator.OnLog += (msg) => {
                    result.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                };
                
                // Log retry attempts
                m_Orchestrator.OnRetryWithNewStrategy += (attempt, analysis) => {
                    result.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] 🔄 RETRY {attempt}/3 - Analyzing failure and trying new strategy");
                    AddLog($"      🔄 Test retry {attempt}/3 - New strategy", new Color(1f, 0.8f, 0.3f));
                };
                
                var plan = await m_Orchestrator.RunTestAsync(testConfig.objective);
                
                int stepsPassed = 0, stepsFailed = 0;
                foreach (var step in plan.Steps)
                {
                    if (step.Status == StepStatus.Passed) stepsPassed++;
                    else if (step.Status == StepStatus.Failed) stepsFailed++;
                }
                
                bool passed = plan.Status == PlanStatus.Completed;
                result.Complete(passed, plan.Status.ToString(), stepsPassed, stepsFailed);
                
                testConfig.RecordRun(passed, stepsPassed, stepsFailed);
            }
            catch (Exception ex)
            {
                result.Complete(false, $"Exception: {ex.Message}", 0, 0);
            }
            
            return result;
        }
        
        private void OnStopClicked()
        {
            if (m_IsRunning)
            {
                m_StopRequested = true;
                m_Orchestrator?.Cancel();
                AddLog("🛑 Stop requested...", Color.yellow);
            }
        }
        
        private void OnOpenLastReportClicked()
        {
            if (m_RunConfig != null && !string.IsNullOrEmpty(m_RunConfig.lastReportPath))
            {
                if (File.Exists(m_RunConfig.lastReportPath))
                {
                    EditorUtility.RevealInFinder(m_RunConfig.lastReportPath);
                    // Also open in default editor
                    System.Diagnostics.Process.Start(m_RunConfig.lastReportPath);
                }
                else
                {
                    AddLog("❌ Report file not found", Color.red);
                }
            }
            else
            {
                AddLog("❌ No report available", Color.red);
            }
        }
        
        private string GenerateReport(int passed, int failed, int skipped, float durationSeconds)
        {
            StringBuilder report = new StringBuilder();
            
            // Header
            report.AppendLine($"# 🏃 Test Run Report: {m_RunConfig.runName}");
            report.AppendLine();
            report.AppendLine($"**Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"**Duration**: {durationSeconds:F1} seconds");
            report.AppendLine();
            
            // Summary
            report.AppendLine("## 📊 Summary");
            report.AppendLine();
            string status = failed == 0 ? "✅ **ALL PASSED**" : "❌ **SOME FAILED**";
            report.AppendLine($"**Result**: {status}");
            report.AppendLine();
            report.AppendLine($"| Metric | Value |");
            report.AppendLine($"|--------|-------|");
            report.AppendLine($"| Tests Passed | {passed} |");
            report.AppendLine($"| Tests Failed | {failed} |");
            report.AppendLine($"| Tests Skipped | {skipped} |");
            report.AppendLine($"| Total Duration | {durationSeconds:F1}s |");
            report.AppendLine();
            
            // Results table
            report.AppendLine("## 📋 Test Results");
            report.AppendLine();
            report.AppendLine("| # | Test Name | Objective | Steps | Duration | Status |");
            report.AppendLine("|---|-----------|-----------|-------|----------|--------|");
            
            for (int i = 0; i < m_Results.Count; i++)
            {
                var r = m_Results[i];
                string resultStatus = r.Passed ? "✅ Pass" : "❌ Fail";
                string objective = r.Objective?.Length > 30 ? r.Objective.Substring(0, 30) + "..." : r.Objective;
                report.AppendLine($"| {i + 1} | {r.TestName} | {objective} | {r.StepsPassed}/{r.StepsPassed + r.StepsFailed} | {r.DurationSeconds:F1}s | {resultStatus} |");
            }
            report.AppendLine();
            
            // Detailed results
            if (m_RunConfig.includeDetailedLogs)
            {
                report.AppendLine("## 📜 Detailed Results");
                report.AppendLine();
                
                for (int i = 0; i < m_Results.Count; i++)
                {
                    var r = m_Results[i];
                    string icon = r.Passed ? "✅" : "❌";
                    report.AppendLine($"### {icon} Test {i + 1}: {r.TestName}");
                    report.AppendLine();
                    report.AppendLine($"- **Objective**: {r.Objective}");
                    report.AppendLine($"- **Status**: {(r.Passed ? "PASSED" : "FAILED")}");
                    report.AppendLine($"- **Steps**: {r.StepsPassed} passed, {r.StepsFailed} failed");
                    report.AppendLine($"- **Duration**: {r.DurationSeconds:F1}s");
                    
                    if (!r.Passed && !string.IsNullOrEmpty(r.Summary))
                    {
                        report.AppendLine($"- **Summary**: {r.Summary}");
                    }
                    
                    if (r.LogEntries.Count > 0)
                    {
                        report.AppendLine();
                        report.AppendLine("<details>");
                        report.AppendLine("<summary>Execution Log</summary>");
                        report.AppendLine();
                        report.AppendLine("```");
                        foreach (var log in r.LogEntries)
                        {
                            report.AppendLine(log);
                        }
                        report.AppendLine("```");
                        report.AppendLine("</details>");
                    }
                    report.AppendLine();
                }
            }
            
            // Footer
            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine("*Report generated by Sentinel Test Runner*");
            
            // Save to file
            string directory = Path.Combine(Application.dataPath, m_RunConfig.reportDirectory);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            string fileName = $"{m_RunConfig.runName}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            fileName = SanitizeFileName(fileName);
            string fullPath = Path.Combine(directory, fileName);
            
            File.WriteAllText(fullPath, report.ToString());
            AssetDatabase.Refresh();
            
            return fullPath;
        }
        
        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(' ', '_');
        }
        
        private void AddLog(string message, Color color)
        {
            m_GlobalLogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            
            var logEntry = new Label(message);
            logEntry.style.color = color;
            logEntry.style.fontSize = 11;
            logEntry.style.whiteSpace = WhiteSpace.Normal;
            logEntry.style.marginBottom = 2;
            m_LogContainer.Add(logEntry);
            
            EditorApplication.delayCall += () =>
            {
                if (m_LogScroll != null && m_LogContainer != null && m_LogContainer.childCount > 0)
                {
                    m_LogScroll.ScrollTo(m_LogContainer[m_LogContainer.childCount - 1]);
                }
            };
        }
    }
}
