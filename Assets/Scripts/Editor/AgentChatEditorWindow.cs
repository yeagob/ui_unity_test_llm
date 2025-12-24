using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Models.Context;
using ChatSystem.Models.Agents;
using ChatSystem.Services.Agents;
using ChatSystem.Services.Tools;
using ChatSystem.Services.Tools.Interfaces;

namespace ChatSystem.Editor
{
    public class AgentChatEditorWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        [SerializeField] private AgentConfig m_AgentConfig;
        
        private VisualElement m_MessagesContainer;
        private TextField m_MessageInput;
        private Button m_SendButton;
        private VisualElement m_ToolStatus;
        private Label m_ToolStatusLabel;
        private ScrollView m_MessagesScroll;
        
        private AgentExecutor m_AgentExecutor;
        private ConversationContext m_Context;
        private bool m_IsProcessing;
        
        [MenuItem("Window/LLM/Agent Chat")]
        public static void ShowWindow()
        {
            AgentChatEditorWindow wnd = GetWindow<AgentChatEditorWindow>();
            wnd.titleContent = new GUIContent("LLM Agent Chat");
            wnd.minSize = new Vector2(400, 500);
        }
        
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            
            // Load UXML
            string uxmlPath = "Assets/Editor/UI/AgentChatWindow.uxml";
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            
            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }
            else
            {
                CreateFallbackUI(root);
            }
            
            // Load USS
            string ussPath = "Assets/Editor/UI/AgentChatStyles.uss";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            // Get references
            m_MessagesContainer = root.Q<VisualElement>("messages-container");
            m_MessageInput = root.Q<TextField>("message-input");
            m_SendButton = root.Q<Button>("send-button");
            m_ToolStatus = root.Q<VisualElement>("tool-status");
            m_ToolStatusLabel = root.Q<Label>("tool-status-label");
            m_MessagesScroll = root.Q<ScrollView>("messages-scroll");
            
            // Setup agent config field
            ObjectField agentField = root.Q<ObjectField>("agent-config-field");
            if (agentField != null)
            {
                agentField.objectType = typeof(AgentConfig);
                agentField.value = m_AgentConfig;
                agentField.RegisterValueChangedCallback(OnAgentConfigChanged);
            }
            
            // Setup button
            if (m_SendButton != null)
            {
                m_SendButton.clicked += OnSendClicked;
            }
            
            // Setup input field enter key
            if (m_MessageInput != null)
            {
                m_MessageInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
            }
            
            // Initialize services
            InitializeServices();
            
            // Add welcome message
            AddSystemMessage("Select an Agent Config to start chatting.");
        }
        
        private void CreateFallbackUI(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.padding = new StyleLength(10);
            
            Label title = new Label("LLM Agent Chat");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);
            
            ObjectField agentField = new ObjectField("Agent Config");
            agentField.name = "agent-config-field";
            agentField.objectType = typeof(AgentConfig);
            root.Add(agentField);
            
            ScrollView scroll = new ScrollView();
            scroll.name = "messages-scroll";
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 10;
            scroll.style.marginBottom = 10;
            
            VisualElement container = new VisualElement();
            container.name = "messages-container";
            scroll.Add(container);
            root.Add(scroll);
            
            VisualElement toolStatus = new VisualElement();
            toolStatus.name = "tool-status";
            toolStatus.style.display = DisplayStyle.None;
            Label toolLabel = new Label();
            toolLabel.name = "tool-status-label";
            toolStatus.Add(toolLabel);
            root.Add(toolStatus);
            
            VisualElement inputArea = new VisualElement();
            inputArea.style.flexDirection = FlexDirection.Row;
            
            TextField input = new TextField();
            input.name = "message-input";
            input.multiline = true;
            input.style.flexGrow = 1;
            input.style.minHeight = 40;
            inputArea.Add(input);
            
            Button send = new Button();
            send.name = "send-button";
            send.text = "Send";
            send.style.width = 80;
            inputArea.Add(send);
            
            root.Add(inputArea);
        }
        
        private void InitializeServices()
        {
            m_AgentExecutor = new AgentExecutor();
            m_Context = new ConversationContext("editor-chat-" + Guid.NewGuid().ToString());
            
            // Register non-game toolsets
            m_AgentExecutor.RegisterToolSet(new TravelToolSet());
            m_AgentExecutor.RegisterToolSet(new UserToolSet());
            
            // Register Sentinel testing toolset (uses null root for now - will be set per test)
            m_AgentExecutor.RegisterToolSet(new Sentinel.Tools.SentinelToolSet(() => null));
        }
        
        private void OnAgentConfigChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            m_AgentConfig = evt.newValue as AgentConfig;
            
            if (m_AgentConfig != null)
            {
                // Register the agent
                m_AgentExecutor.RegisterAgent(m_AgentConfig);
                
                // Clear previous conversation
                m_Context = new ConversationContext("editor-chat-" + Guid.NewGuid().ToString());
                ClearMessages();
                
                AddSystemMessage($"Agent '{m_AgentConfig.agentId}' loaded. Ready to chat!");
            }
        }
        
        private void OnSendClicked()
        {
            SendMessage();
        }
        
        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
            {
                evt.PreventDefault();
                SendMessage();
            }
        }
        
        private async void SendMessage()
        {
            if (m_IsProcessing) return;
            if (m_AgentConfig == null)
            {
                AddSystemMessage("Please select an Agent Config first.");
                return;
            }
            
            string message = m_MessageInput?.value?.Trim();
            if (string.IsNullOrEmpty(message)) return;
            
            // Clear input
            m_MessageInput.value = "";
            
            // Add user message to UI
            AddUserMessage(message);
            
            // Add to context
            m_Context.AddUserMessage(message);
            
            // Process with agent
            await ProcessAgentResponseAsync();
        }
        
        private async Task ProcessAgentResponseAsync()
        {
            m_IsProcessing = true;
            SetLoading(true);
            ShowToolStatus("Processing...");
            
            try
            {
                AgentResponse response = await m_AgentExecutor.ExecuteAgentAsync(
                    m_AgentConfig.agentId, 
                    m_Context
                );
                
                if (response.success)
                {
                    // Show tool calls if any
                    if (response.toolCalls != null && response.toolCalls.Count > 0)
                    {
                        foreach (var toolCall in response.toolCalls)
                        {
                            AddToolMessage($"🔧 Tool: {toolCall.name}");
                        }
                    }
                    
                    // Add assistant response
                    if (!string.IsNullOrEmpty(response.content))
                    {
                        AddAssistantMessage(response.content);
                        m_Context.AddAssistantMessage(response.content);
                    }
                }
                else
                {
                    AddSystemMessage($"Error: {response.content}");
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage($"Error: {ex.Message}");
                Debug.LogError($"Agent execution error: {ex}");
            }
            finally
            {
                m_IsProcessing = false;
                SetLoading(false);
                HideToolStatus();
            }
        }
        
        private void AddUserMessage(string text)
        {
            AddMessage(text, "user-message");
        }
        
        private void AddAssistantMessage(string text)
        {
            AddMessage(text, "assistant-message");
        }
        
        private void AddToolMessage(string text)
        {
            AddMessage(text, "tool-message");
        }
        
        private void AddSystemMessage(string text)
        {
            AddMessage(text, "system-message");
        }
        
        private void AddMessage(string text, string className)
        {
            if (m_MessagesContainer == null) return;
            
            Label message = new Label(text);
            message.AddToClassList("message-bubble");
            message.AddToClassList(className);
            message.style.whiteSpace = WhiteSpace.Normal;
            
            m_MessagesContainer.Add(message);
            
            // Scroll to bottom
            EditorApplication.delayCall += () =>
            {
                m_MessagesScroll?.ScrollTo(message);
            };
        }
        
        private void ClearMessages()
        {
            m_MessagesContainer?.Clear();
        }
        
        private void ShowToolStatus(string text)
        {
            if (m_ToolStatus != null)
            {
                m_ToolStatus.style.display = DisplayStyle.Flex;
                if (m_ToolStatusLabel != null)
                {
                    m_ToolStatusLabel.text = text;
                }
            }
        }
        
        private void HideToolStatus()
        {
            if (m_ToolStatus != null)
            {
                m_ToolStatus.style.display = DisplayStyle.None;
            }
        }
        
        private void SetLoading(bool isLoading)
        {
            if (m_SendButton != null)
            {
                m_SendButton.SetEnabled(!isLoading);
                m_SendButton.text = isLoading ? "..." : "Send";
            }
            
            if (m_MessageInput != null)
            {
                m_MessageInput.SetEnabled(!isLoading);
            }
        }
    }
}
