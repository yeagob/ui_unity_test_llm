using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Models.Context;
using ChatSystem.Services.Agents;
using ChatSystem.Services.Tools;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Tools;
using Sentinel.Configuration;
namespace Sentinel.Editor
{
    /// <summary>
    /// Editor Window for the Multi-Agent System (MAS) Sentinel Testing.
    /// Provides a visual interface for running automated UI tests with 3 specialized agents.
    /// </summary>
    public class MASEditorWindow : EditorWindow
    {
        [SerializeField] private AgentConfig m_PlannerConfig;
        [SerializeField] private AgentConfig m_ExecutorConfig;
        [SerializeField] private AgentConfig m_VerifierConfig;
        [SerializeField] private TestConfiguration m_TestConfig;
        
        private TextField m_ObjectiveInput;
        private Button m_RunButton;
        private Button m_StopButton;
        private VisualElement m_LogContainer;
        private ScrollView m_LogScroll;
        private VisualElement m_PlanContainer;
        private Label m_StatusLabel;
        private ProgressBar m_ProgressBar;
        
        private MASOrchestrator m_Orchestrator;
        private AgentExecutor m_Executor;
        private TestPlan m_CurrentPlan;
        private bool m_IsRunning;
        
        // For report generation
        private List<string> m_LogEntries = new List<string>();
        private DateTime m_TestStartTime;
        private DateTime m_TestEndTime;
        
        // Persistence keys
        private const string PREF_KEY_OBJECTIVE = "MAS_TestObjective";
        private const string PREF_KEY_PLANNER = "MAS_PlannerConfig";
        private const string PREF_KEY_EXECUTOR = "MAS_ExecutorConfig";
        private const string PREF_KEY_VERIFIER = "MAS_VerifierConfig";
        
        [MenuItem("Window/LLM/MAS Testing")]
        public static void ShowWindow()
        {
            MASEditorWindow wnd = GetWindow<MASEditorWindow>();
            wnd.titleContent = new GUIContent("🤖 MAS Testing");
            wnd.minSize = new Vector2(500, 600);
        }
        
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
            
            // Header
            Label title = new Label("🤖 Multi-Agent System Testing");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.4f, 0.8f, 1f);
            title.style.marginBottom = 15;
            root.Add(title);
            
            // Quick Load from TestConfiguration
            VisualElement quickLoadRow = new VisualElement();
            quickLoadRow.style.flexDirection = FlexDirection.Row;
            quickLoadRow.style.marginBottom = 10;
            
            ObjectField testConfigField = new ObjectField("📂 Load Test Config");
            testConfigField.objectType = typeof(TestConfiguration);
            testConfigField.style.flexGrow = 1;
            testConfigField.value = m_TestConfig;
            testConfigField.RegisterValueChangedCallback(evt => {
                m_TestConfig = evt.newValue as TestConfiguration;
                if (m_TestConfig != null) LoadFromTestConfig(m_TestConfig);
            });
            quickLoadRow.Add(testConfigField);
            root.Add(quickLoadRow);
            
            // Agent Configurations
            Foldout agentsFoldout = new Foldout();
            agentsFoldout.text = "⚙️ Agent Configurations";
            agentsFoldout.value = false;
            
            ObjectField plannerField = new ObjectField("Planner Agent");
            plannerField.name = "planner-field";
            plannerField.objectType = typeof(AgentConfig);
            plannerField.value = m_PlannerConfig;
            plannerField.RegisterValueChangedCallback(evt => m_PlannerConfig = evt.newValue as AgentConfig);
            agentsFoldout.Add(plannerField);
            
            ObjectField executorField = new ObjectField("Executor Agent");
            executorField.name = "executor-field";
            executorField.objectType = typeof(AgentConfig);
            executorField.value = m_ExecutorConfig;
            executorField.RegisterValueChangedCallback(evt => m_ExecutorConfig = evt.newValue as AgentConfig);
            agentsFoldout.Add(executorField);
            
            ObjectField verifierField = new ObjectField("Verifier Agent");
            verifierField.name = "verifier-field";
            verifierField.objectType = typeof(AgentConfig);
            verifierField.value = m_VerifierConfig;
            verifierField.RegisterValueChangedCallback(evt => m_VerifierConfig = evt.newValue as AgentConfig);
            agentsFoldout.Add(verifierField);
            
            root.Add(agentsFoldout);
            
            // Objective Input
            Label objectiveLabel = new Label("🎯 Test Objective");
            objectiveLabel.style.marginTop = 15;
            objectiveLabel.style.fontSize = 14;
            objectiveLabel.style.color = Color.white;
            root.Add(objectiveLabel);
            
            m_ObjectiveInput = new TextField();
            m_ObjectiveInput.multiline = true;
            m_ObjectiveInput.style.minHeight = 60;
            m_ObjectiveInput.style.marginTop = 5;
            m_ObjectiveInput.style.marginBottom = 10;
            // Load persisted objective
            m_ObjectiveInput.value = EditorPrefs.GetString(PREF_KEY_OBJECTIVE, "Test that the Play button starts the game correctly");
            m_ObjectiveInput.RegisterValueChangedCallback(evt => EditorPrefs.SetString(PREF_KEY_OBJECTIVE, evt.newValue));
            root.Add(m_ObjectiveInput);
            
            // Control Buttons
            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginBottom = 15;
            
            m_RunButton = new Button(OnRunClicked);
            m_RunButton.text = "▶️ Run Test";
            m_RunButton.style.flexGrow = 1;
            m_RunButton.style.height = 35;
            m_RunButton.style.backgroundColor = new Color(0.2f, 0.6f, 0.3f);
            buttonRow.Add(m_RunButton);
            
            m_StopButton = new Button(OnStopClicked);
            m_StopButton.text = "⏹️ Stop";
            m_StopButton.style.width = 80;
            m_StopButton.style.height = 35;
            m_StopButton.style.marginLeft = 10;
            m_StopButton.style.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
            m_StopButton.style.display = DisplayStyle.None;
            buttonRow.Add(m_StopButton);
            
            // Copy Report button
            Button copyButton = new Button(OnCopyReportClicked);
            copyButton.text = "📋 Copy Report";
            copyButton.style.width = 110;
            copyButton.style.height = 35;
            copyButton.style.marginLeft = 10;
            copyButton.tooltip = "Copy full test report to clipboard";
            buttonRow.Add(copyButton);
            
            root.Add(buttonRow);
            
            // Status Bar
            m_StatusLabel = new Label("Ready");
            m_StatusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            m_StatusLabel.style.marginBottom = 5;
            root.Add(m_StatusLabel);
            
            m_ProgressBar = new ProgressBar();
            m_ProgressBar.title = "Progress";
            m_ProgressBar.style.height = 20;
            m_ProgressBar.style.marginBottom = 10;
            m_ProgressBar.style.display = DisplayStyle.None;
            root.Add(m_ProgressBar);
            
            // Plan Display
            Foldout planFoldout = new Foldout();
            planFoldout.text = "📋 Test Plan";
            planFoldout.value = true;
            
            m_PlanContainer = new VisualElement();
            m_PlanContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
            m_PlanContainer.style.paddingTop = 5;
            m_PlanContainer.style.paddingBottom = 5;
            m_PlanContainer.style.paddingLeft = 10;
            m_PlanContainer.style.paddingRight = 10;
            m_PlanContainer.style.borderTopLeftRadius = 5;
            m_PlanContainer.style.borderTopRightRadius = 5;
            m_PlanContainer.style.borderBottomLeftRadius = 5;
            m_PlanContainer.style.borderBottomRightRadius = 5;
            
            Label noPlanLabel = new Label("No plan yet. Run a test to see the plan.");
            noPlanLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            noPlanLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            m_PlanContainer.Add(noPlanLabel);
            
            planFoldout.Add(m_PlanContainer);
            root.Add(planFoldout);
            
            // Log Output
            Label logLabel = new Label("📜 Execution Log");
            logLabel.style.marginTop = 15;
            logLabel.style.fontSize = 14;
            logLabel.style.color = Color.white;
            root.Add(logLabel);
            
            m_LogScroll = new ScrollView();
            m_LogScroll.style.flexGrow = 1;
            m_LogScroll.style.marginTop = 5;
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
            
            // Initialize services
            InitializeServices();
        }
        
        private void InitializeServices()
        {
            m_Executor = new AgentExecutor();
            
            // Register Sentinel toolset
            m_Executor.RegisterToolSet(new SentinelToolSet(GetCurrentUIRoot));
            
            AddLog("🟢 MAS System initialized", Color.green);
            AddLog("Configure the 3 agents and enter a test objective to begin.", Color.gray);
        }
        
        private void LoadFromTestConfig(TestConfiguration config)
        {
            if (config == null) return;
            
            // Load agents
            m_PlannerConfig = config.plannerAgent;
            m_ExecutorConfig = config.executorAgent;
            m_VerifierConfig = config.verifierAgent;
            
            // Load objective
            if (!string.IsNullOrEmpty(config.objective))
            {
                m_ObjectiveInput.value = config.objective;
            }
            
            // Update UI fields if they exist
            var plannerField = rootVisualElement.Q<ObjectField>("planner-field");
            if (plannerField != null) plannerField.value = m_PlannerConfig;
            
            var executorField = rootVisualElement.Q<ObjectField>("executor-field");
            if (executorField != null) executorField.value = m_ExecutorConfig;
            
            var verifierField = rootVisualElement.Q<ObjectField>("verifier-field");
            if (verifierField != null) verifierField.value = m_VerifierConfig;
            
            AddLog($"📂 Loaded test config: {config.testName}", Color.cyan);
            
            if (!string.IsNullOrEmpty(config.lastRunResult))
            {
                AddLog($"   Last run: {config.lastRunResult} ({config.lastRunPassedSteps} passed, {config.lastRunFailedSteps} failed)", Color.gray);
            }
        }
        
        private VisualElement GetCurrentUIRoot()
        {
            var uiDocument = UnityEngine.Object.FindObjectOfType<UIDocument>();
            return uiDocument?.rootVisualElement;
        }
        
        private async void OnRunClicked()
        {
            if (m_IsRunning) return;
            
            // Validate configuration
            if (m_PlannerConfig == null || m_ExecutorConfig == null || m_VerifierConfig == null)
            {
                AddLog("❌ Please configure all 3 agents before running", Color.red);
                return;
            }
            
            string objective = m_ObjectiveInput.value?.Trim();
            if (string.IsNullOrEmpty(objective))
            {
                AddLog("❌ Please enter a test objective", Color.red);
                return;
            }
            
            m_IsRunning = true;
            m_RunButton.style.display = DisplayStyle.None;
            m_StopButton.style.display = DisplayStyle.Flex;
            m_ProgressBar.style.display = DisplayStyle.Flex;
            m_ProgressBar.value = 0;
            m_LogContainer.Clear();
            m_PlanContainer.Clear();
            
            // Clear previous logs and set start time
            m_LogEntries.Clear();
            m_TestStartTime = DateTime.Now;
            m_TestEndTime = DateTime.MinValue;
            
            // Create orchestrator
            m_Orchestrator = new MASOrchestrator(
                m_Executor,
                m_PlannerConfig,
                m_ExecutorConfig,
                m_VerifierConfig
            );
            
            // Subscribe to events
            m_Orchestrator.OnLog += OnOrchestratorLog;
            m_Orchestrator.OnPlanCreated += OnPlanCreated;
            m_Orchestrator.OnStepStarted += OnStepStarted;
            m_Orchestrator.OnStepCompleted += OnStepCompleted;
            m_Orchestrator.OnTestCompleted += OnTestCompleted;
            m_Orchestrator.OnRetryWithNewStrategy += OnRetryWithNewStrategy;
            
            try
            {
                m_StatusLabel.text = "Running...";
                m_CurrentPlan = await m_Orchestrator.RunTestAsync(objective);
                
                // Final status
                switch (m_CurrentPlan.Status)
                {
                    case PlanStatus.Completed:
                        m_StatusLabel.text = "✅ Test PASSED";
                        m_StatusLabel.style.color = Color.green;
                        break;
                    case PlanStatus.Failed:
                        m_StatusLabel.text = "❌ Test FAILED";
                        m_StatusLabel.style.color = Color.red;
                        break;
                    case PlanStatus.Cancelled:
                        m_StatusLabel.text = "🛑 Test Cancelled";
                        m_StatusLabel.style.color = Color.yellow;
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error: {ex.Message}", Color.red);
                m_StatusLabel.text = "Error";
                m_StatusLabel.style.color = Color.red;
            }
            finally
            {
                m_IsRunning = false;
                m_RunButton.style.display = DisplayStyle.Flex;
                m_StopButton.style.display = DisplayStyle.None;
            }
        }
        
        private void OnStopClicked()
        {
            if (m_Orchestrator != null && m_IsRunning)
            {
                m_Orchestrator.Cancel();
                AddLog("🛑 Stop requested...", Color.yellow);
            }
        }
        
        private void OnOrchestratorLog(string message)
        {
            Color color = Color.white;
            if (message.Contains("✅")) color = Color.green;
            else if (message.Contains("❌")) color = Color.red;
            else if (message.Contains("⚠️") || message.Contains("🛑")) color = Color.yellow;
            else if (message.Contains("📋") || message.Contains("🔧")) color = new Color(0.5f, 0.8f, 1f);
            
            AddLog(message, color);
        }
        
        private void OnPlanCreated(TestPlan plan)
        {
            m_PlanContainer.Clear();
            
            Label criteriaLabel = new Label($"✓ Success Criteria: {plan.SuccessCriteria}");
            criteriaLabel.style.color = new Color(0.7f, 0.9f, 0.7f);
            criteriaLabel.style.marginBottom = 10;
            m_PlanContainer.Add(criteriaLabel);
            
            foreach (var step in plan.Steps)
            {
                var stepLabel = new Label($"  {step.StepNumber}. {step.Action}({step.Target}) → {step.ExpectedResult}");
                stepLabel.name = $"step-{step.StepNumber}";
                stepLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                stepLabel.style.marginBottom = 3;
                m_PlanContainer.Add(stepLabel);
            }
            
            m_ProgressBar.highValue = plan.Steps.Count;
        }
        
        private void OnStepStarted(TestStep step)
        {
            var stepLabel = m_PlanContainer.Q<Label>($"step-{step.StepNumber}");
            if (stepLabel != null)
            {
                stepLabel.style.color = new Color(1f, 0.9f, 0.5f);
                stepLabel.text = $"▶ {step.StepNumber}. {step.Action}({step.Target}) → {step.ExpectedResult}";
            }
        }
        
        private void OnStepCompleted(TestStep step, VerificationResult result)
        {
            var stepLabel = m_PlanContainer.Q<Label>($"step-{step.StepNumber}");
            if (stepLabel != null)
            {
                if (step.Status == StepStatus.Passed)
                {
                    stepLabel.style.color = Color.green;
                    stepLabel.text = $"✅ {step.StepNumber}. {step.Action}({step.Target})";
                }
                else
                {
                    stepLabel.style.color = Color.red;
                    stepLabel.text = $"❌ {step.StepNumber}. {step.Action}({step.Target}) - {result?.Diagnosis ?? "Failed"}";
                }
            }
            
            m_ProgressBar.value = step.StepNumber;
        }
        
        private void OnTestCompleted(TestPlan plan)
        {
            m_ProgressBar.value = m_ProgressBar.highValue;
            m_TestEndTime = DateTime.Now;
        }
        
        private void OnRetryWithNewStrategy(int attempt, string analysis)
        {
            // Clear previous plan display for new attempt
            m_PlanContainer.Clear();
            
            Label retryLabel = new Label($"🔄 Attempt {attempt}/3 - Analyzing failure and creating new strategy...");
            retryLabel.style.color = new Color(1f, 0.8f, 0.3f);
            retryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            retryLabel.style.marginBottom = 10;
            m_PlanContainer.Add(retryLabel);
            
            m_ProgressBar.value = 0;
            m_StatusLabel.text = $"Retry {attempt}/3 - Creating new plan...";
        }
        
        private void AddLog(string message, Color color)
        {
            string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            m_LogEntries.Add(logLine);
            
            var logEntry = new Label(logLine);
            logEntry.style.color = color;
            logEntry.style.fontSize = 11;
            logEntry.style.whiteSpace = WhiteSpace.Normal;
            logEntry.style.marginBottom = 2;
            m_LogContainer.Add(logEntry);
            
            // Auto-scroll to bottom
            EditorApplication.delayCall += () =>
            {
                if (m_LogScroll != null && m_LogContainer != null && m_LogContainer.childCount > 0)
                {
                    m_LogScroll.ScrollTo(m_LogContainer[m_LogContainer.childCount - 1]);
                }
            };
        }
        
        private void OnCopyReportClicked()
        {
            StringBuilder report = new StringBuilder();
            
            // Header
            report.AppendLine("# 🤖 MAS Test Report");
            report.AppendLine($"**Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();
            
            // Test Request
            report.AppendLine("## 🎯 Test Objective");
            report.AppendLine("```");
            report.AppendLine(m_ObjectiveInput?.value ?? "No objective");
            report.AppendLine("```");
            report.AppendLine();
            
            // Agent Configuration
            report.AppendLine("## ⚙️ Agent Configuration");
            report.AppendLine($"- **Planner**: {m_PlannerConfig?.agentName ?? "Not configured"}");
            report.AppendLine($"- **Executor**: {m_ExecutorConfig?.agentName ?? "Not configured"}");
            report.AppendLine($"- **Verifier**: {m_VerifierConfig?.agentName ?? "Not configured"}");
            report.AppendLine();
            
            // Test Plan
            report.AppendLine("## 📋 Test Plan");
            if (m_CurrentPlan != null && m_CurrentPlan.Steps.Count > 0)
            {
                report.AppendLine($"**Success Criteria**: {m_CurrentPlan.SuccessCriteria}");
                report.AppendLine();
                report.AppendLine("| Step | Action | Target | Expected | Status |");
                report.AppendLine("|------|--------|--------|----------|--------|");
                foreach (var step in m_CurrentPlan.Steps)
                {
                    string status = step.Status switch
                    {
                        StepStatus.Passed => "✅ Passed",
                        StepStatus.Failed => "❌ Failed",
                        StepStatus.Skipped => "⏭️ Skipped",
                        StepStatus.Pending => "⏳ Pending",
                        _ => step.Status.ToString()
                    };
                    report.AppendLine($"| {step.StepNumber} | {step.Action} | {step.Target} | {step.ExpectedResult} | {status} |");
                }
            }
            else
            {
                report.AppendLine("*No plan generated*");
            }
            report.AppendLine();
            
            // Result Summary
            report.AppendLine("## 📊 Result");
            if (m_CurrentPlan != null)
            {
                string result = m_CurrentPlan.Status switch
                {
                    PlanStatus.Completed => "✅ **PASSED**",
                    PlanStatus.Failed => "❌ **FAILED**",
                    PlanStatus.Cancelled => "🛑 **CANCELLED**",
                    _ => m_CurrentPlan.Status.ToString()
                };
                report.AppendLine(result);
                
                int passed = 0, failed = 0;
                foreach (var step in m_CurrentPlan.Steps)
                {
                    if (step.Status == StepStatus.Passed) passed++;
                    else if (step.Status == StepStatus.Failed) failed++;
                }
                report.AppendLine($"- Steps Passed: {passed}/{m_CurrentPlan.Steps.Count}");
                report.AppendLine($"- Steps Failed: {failed}");
                if (m_TestEndTime > m_TestStartTime)
                {
                    report.AppendLine($"- Duration: {(m_TestEndTime - m_TestStartTime):mm\\:ss}");
                }
            }
            else
            {
                report.AppendLine("*No test executed*");
            }
            report.AppendLine();
            
            // Execution Log
            report.AppendLine("## 📜 Execution Log");
            report.AppendLine("```");
            foreach (var log in m_LogEntries)
            {
                report.AppendLine(log);
            }
            report.AppendLine("```");
            report.AppendLine();
            
            // Footer
            report.AppendLine("---");
            report.AppendLine("*Report generated by Sentinel MAS Testing System*");
            
            // Copy to clipboard
            GUIUtility.systemCopyBuffer = report.ToString();
            
            AddLog("📋 Report copied to clipboard!", Color.cyan);
            Debug.Log("[MAS] Full report copied to clipboard");
        }
    }
}
