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
        private Button m_ClearButton;
        private VisualElement m_ToolStatus;
        private Label m_ToolStatusLabel;
        private ScrollView m_MessagesScroll;
        private Toggle m_DebugToggle;
        private VisualElement m_LoadingIndicator;
        private Label m_CharCountLabel;
        
        private AgentExecutor m_AgentExecutor;
        private ConversationContext m_Context;
        private bool m_IsProcessing;
        private bool m_DebugMode = true;
        private const int MAX_MESSAGE_LENGTH = 4000;
        
        [MenuItem("Window/LLM/Agent Chat")]
        public static void ShowWindow()
        {
            AgentChatEditorWindow wnd = GetWindow<AgentChatEditorWindow>();
            wnd.titleContent = new GUIContent("LLM Agent Chat");
            wnd.minSize = new Vector2(450, 600);
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
            m_ClearButton = root.Q<Button>("clear-button");
            m_ToolStatus = root.Q<VisualElement>("tool-status");
            m_ToolStatusLabel = root.Q<Label>("tool-status-label");
            m_MessagesScroll = root.Q<ScrollView>("messages-scroll");
            m_DebugToggle = root.Q<Toggle>("debug-toggle");
            m_CharCountLabel = root.Q<Label>("char-count-label");
            
            // Setup debug toggle
            if (m_DebugToggle != null)
            {
                m_DebugToggle.value = m_DebugMode;
                m_DebugToggle.RegisterValueChangedCallback(evt => m_DebugMode = evt.newValue);
            }
            
            // Setup agent config field
            ObjectField agentField = root.Q<ObjectField>("agent-config-field");
            if (agentField != null)
            {
                agentField.objectType = typeof(AgentConfig);
                agentField.value = m_AgentConfig;
                agentField.RegisterValueChangedCallback(OnAgentConfigChanged);
            }
            
            // Setup send button
            if (m_SendButton != null)
            {
                m_SendButton.clicked += OnSendClicked;
                m_SendButton.SetEnabled(false); // Disabled by default
                m_SendButton.tooltip = "Send message (Enter)";
            }
            
            // Setup clear button
            if (m_ClearButton != null)
            {
                m_ClearButton.clicked += OnClearClicked;
                m_ClearButton.tooltip = "Clear conversation";
            }
            
            // Setup input field with all event handlers
            if (m_MessageInput != null)
            {
                // Use TrickleDown to capture Enter before TextField handles it
                m_MessageInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
                m_MessageInput.RegisterValueChangedCallback(OnInputChanged);
                m_MessageInput.tooltip = "Type your message here\nEnter: Send\nCtrl+Enter: New line";
                
                // Force white text color on the input
                m_MessageInput.style.color = Color.white;
                var textInput = m_MessageInput.Q<VisualElement>("unity-text-input");
                if (textInput != null)
                {
                    textInput.style.color = Color.white;
                }
                // Also try to style the text element directly
                m_MessageInput.RegisterCallback<GeometryChangedEvent>(evt => 
                {
                    var textElements = m_MessageInput.Query<TextElement>().ToList();
                    foreach (var te in textElements)
                    {
                        te.style.color = Color.white;
                    }
                });
            }
            
            // Initialize services
            InitializeServices();
            
            // Register agent if already assigned (persisted from previous session)
            if (m_AgentConfig != null)
            {
                m_AgentExecutor.RegisterAgent(m_AgentConfig);
                AddSystemMessage($"✅ Agent '{m_AgentConfig.agentName}' restored. Ready to chat!");
            }
            else
            {
                AddSystemMessage("👋 Welcome! Select an Agent Config to start chatting.");
            }
            
            // Update button state
            UpdateSendButtonState();
        }
        
        private void CreateFallbackUI(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.backgroundColor = new Color(0.1f, 0.1f, 0.18f);
            
            // Header
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 10;
            
            Label title = new Label("🤖 LLM Agent Chat");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.91f, 0.27f, 0.38f);
            header.Add(title);
            
            VisualElement headerControls = new VisualElement();
            headerControls.style.flexDirection = FlexDirection.Row;
            headerControls.style.alignItems = Align.Center;
            
            Toggle debugToggle = new Toggle("Debug");
            debugToggle.name = "debug-toggle";
            debugToggle.value = true;
            headerControls.Add(debugToggle);
            
            Button clearBtn = new Button(() => OnClearClicked());
            clearBtn.name = "clear-button";
            clearBtn.text = "🗑";
            clearBtn.tooltip = "Clear conversation";
            clearBtn.style.marginLeft = 8;
            clearBtn.style.width = 28;
            clearBtn.style.height = 28;
            headerControls.Add(clearBtn);
            
            header.Add(headerControls);
            root.Add(header);
            
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
            
            // Input area with char count
            VisualElement inputWrapper = new VisualElement();
            
            VisualElement inputArea = new VisualElement();
            inputArea.style.flexDirection = FlexDirection.Row;
            inputArea.style.alignItems = Align.FlexEnd;
            
            TextField input = new TextField();
            input.name = "message-input";
            input.multiline = true;
            input.style.flexGrow = 1;
            input.style.minHeight = 44;
            input.style.maxHeight = 120;
            inputArea.Add(input);
            
            Button send = new Button();
            send.name = "send-button";
            send.text = "➤";
            send.style.width = 44;
            send.style.height = 44;
            send.style.marginLeft = 8;
            inputArea.Add(send);
            
            inputWrapper.Add(inputArea);
            
            // Char count and hint
            VisualElement inputFooter = new VisualElement();
            inputFooter.style.flexDirection = FlexDirection.Row;
            inputFooter.style.justifyContent = Justify.SpaceBetween;
            inputFooter.style.marginTop = 4;
            
            Label hint = new Label("Enter: send • Ctrl+Enter: new line");
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.5f, 0.5f, 0.5f);
            inputFooter.Add(hint);
            
            Label charCount = new Label("0 / " + MAX_MESSAGE_LENGTH);
            charCount.name = "char-count-label";
            charCount.style.fontSize = 10;
            charCount.style.color = new Color(0.5f, 0.5f, 0.5f);
            inputFooter.Add(charCount);
            
            inputWrapper.Add(inputFooter);
            root.Add(inputWrapper);
        }
        
        private void SetPlaceholder(TextField field, string placeholder)
        {
            // UI Toolkit doesn't have native placeholder, so we simulate it
            if (string.IsNullOrEmpty(field.value))
            {
                field.Q<TextElement>()?.AddToClassList("placeholder-text");
            }
        }
        
        private void InitializeServices()
        {
            m_AgentExecutor = new AgentExecutor();
            m_Context = new ConversationContext("editor-chat-" + Guid.NewGuid().ToString());
            
            // Register non-game toolsets
            m_AgentExecutor.RegisterToolSet(new TravelToolSet());
            m_AgentExecutor.RegisterToolSet(new UserToolSet());
            
            // Register Sentinel testing toolset with dynamic UI root detection
            m_AgentExecutor.RegisterToolSet(new Sentinel.Tools.SentinelToolSet(GetCurrentUIRoot));
        }
        
        /// <summary>
        /// Finds the current UI root - checks for UIDocument in scene or falls back to finding UI Toolkit panels.
        /// </summary>
        private VisualElement GetCurrentUIRoot()
        {
            // Try to find UIDocument in the scene (Play Mode UI Toolkit)
            var uiDocument = UnityEngine.Object.FindObjectOfType<UIDocument>();
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                return uiDocument.rootVisualElement;
            }
            
            // For Editor windows, we could return our own root but that's not useful for testing
            // Return null and let the tool report appropriately
            return null;
        }
        
        private void OnAgentConfigChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            m_AgentConfig = evt.newValue as AgentConfig;
            
            if (m_AgentConfig != null)
            {
                m_AgentExecutor.RegisterAgent(m_AgentConfig);
                m_Context = new ConversationContext("editor-chat-" + Guid.NewGuid().ToString());
                ClearMessages();
                AddSystemMessage($"✅ Agent '{m_AgentConfig.agentName}' loaded. Ready to chat!");
            }
            
            UpdateSendButtonState();
        }
        
        private void OnInputChanged(ChangeEvent<string> evt)
        {
            UpdateSendButtonState();
            UpdateCharCount();
        }
        
        private void UpdateSendButtonState()
        {
            if (m_SendButton == null) return;
            
            string text = m_MessageInput?.value?.Trim() ?? "";
            bool hasText = !string.IsNullOrEmpty(text);
            bool hasAgent = m_AgentConfig != null;
            bool notProcessing = !m_IsProcessing;
            bool notTooLong = text.Length <= MAX_MESSAGE_LENGTH;
            
            m_SendButton.SetEnabled(hasText && hasAgent && notProcessing && notTooLong);
            
            // Visual feedback
            if (!hasAgent)
            {
                m_SendButton.tooltip = "Select an Agent Config first";
            }
            else if (!hasText)
            {
                m_SendButton.tooltip = "Type a message to send";
            }
            else if (!notTooLong)
            {
                m_SendButton.tooltip = "Message too long";
            }
            else if (!notProcessing)
            {
                m_SendButton.tooltip = "Processing...";
            }
            else
            {
                m_SendButton.tooltip = "Send message (Enter)";
            }
        }
        
        private void UpdateCharCount()
        {
            if (m_CharCountLabel == null) return;
            
            int length = m_MessageInput?.value?.Length ?? 0;
            m_CharCountLabel.text = $"{length} / {MAX_MESSAGE_LENGTH}";
            
            // Color feedback
            if (length > MAX_MESSAGE_LENGTH)
            {
                m_CharCountLabel.style.color = new Color(0.9f, 0.3f, 0.3f);
            }
            else if (length > MAX_MESSAGE_LENGTH * 0.9f)
            {
                m_CharCountLabel.style.color = new Color(0.9f, 0.7f, 0.3f);
            }
            else
            {
                m_CharCountLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            }
        }
        
        private void OnSendClicked()
        {
            SendMessage();
        }
        
        private void OnClearClicked()
        {
            if (m_IsProcessing) return;
            
            m_Context = new ConversationContext("editor-chat-" + Guid.NewGuid().ToString());
            ClearMessages();
            AddSystemMessage("🧹 Conversation cleared.");
        }
        
        private void OnInputKeyDown(KeyDownEvent evt)
        {
            // Ctrl+Enter or Shift+Enter = new line (let it through)
            if (evt.keyCode == KeyCode.Return && (evt.ctrlKey || evt.shiftKey))
            {
                // Insert newline manually since we're handling Enter
                if (m_MessageInput != null)
                {
                    string current = m_MessageInput.value ?? "";
                    int cursorPos = Mathf.Clamp(m_MessageInput.cursorIndex, 0, current.Length);
                    m_MessageInput.value = current.Insert(cursorPos, "\n");
                    
                    // Move cursor after newline
                    int newPos = cursorPos + 1;
                    EditorApplication.delayCall += () =>
                    {
                        if (m_MessageInput != null)
                        {
                            m_MessageInput.SelectRange(newPos, newPos);
                        }
                    };
                }
                evt.StopPropagation();
                return;
            }
            
            // Enter = send message
            if (evt.keyCode == KeyCode.Return)
            {
                evt.PreventDefault();
                evt.StopPropagation();
                
                if (m_SendButton != null && m_SendButton.enabledSelf)
                {
                    SendMessage();
                }
                return;
            }
            
            // Escape = clear input
            if (evt.keyCode == KeyCode.Escape)
            {
                if (m_MessageInput != null)
                {
                    m_MessageInput.value = "";
                    m_MessageInput.Blur();
                }
                evt.StopPropagation();
            }
        }
        
        private async void SendMessage()
        {
            if (m_IsProcessing) return;
            if (m_AgentConfig == null)
            {
                AddSystemMessage("⚠️ Please select an Agent Config first.");
                return;
            }
            
            string message = m_MessageInput?.value?.Trim();
            if (string.IsNullOrEmpty(message)) return;
            
            if (message.Length > MAX_MESSAGE_LENGTH)
            {
                AddSystemMessage($"⚠️ Message too long ({message.Length}/{MAX_MESSAGE_LENGTH})");
                return;
            }
            
            // Clear input immediately for better UX
            m_MessageInput.value = "";
            m_MessageInput.Focus();
            UpdateSendButtonState();
            
            AddUserMessage(message);
            m_Context.AddUserMessage(message);
            
            await ProcessAgentResponseAsync();
        }
        
        private async Task ProcessAgentResponseAsync()
        {
            m_IsProcessing = true;
            UpdateSendButtonState();
            
            // Show loading indicator
            ShowLoadingIndicator();
            
            if (m_DebugMode)
            {
                AddDebugMessage("🧠 Thinking...", "debug-thinking");
            }
            
            try
            {
                // Pass AgentConfig directly - handles auto-registration
                AgentResponse response = await m_AgentExecutor.ExecuteAgentAsync(
                    m_AgentConfig, 
                    m_Context
                );
                
                // Remove loading indicator
                HideLoadingIndicator();
                
                if (response.success)
                {
                    // Show tool calls in debug mode
                    if (response.toolCalls != null && response.toolCalls.Count > 0)
                    {
                        foreach (var toolCall in response.toolCalls)
                        {
                            if (m_DebugMode)
                            {
                                string args = SerializeArgs(toolCall.arguments);
                                AddDebugMessage($"🔧 {toolCall.name}({args})", "debug-tool-call");
                            }
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
                    AddSystemMessage($"❌ Error: {response.content}");
                }
            }
            catch (Exception ex)
            {
                HideLoadingIndicator();
                AddSystemMessage($"❌ Error: {ex.Message}");
                Debug.LogError($"Agent execution error: {ex}");
            }
            finally
            {
                m_IsProcessing = false;
                UpdateSendButtonState();
            }
        }
        
        private void AddUserMessage(string text)
        {
            if (m_MessagesContainer == null) return;
            
            VisualElement row = CreateMessageRow(true);
            VisualElement bubble = CreateMessageBubble(text, "user-message");
            row.Add(bubble);
            row.Add(CreateAvatar("👤", "avatar-user"));
            
            m_MessagesContainer.Add(row);
            ScrollToBottom();
        }
        
        private void AddAssistantMessage(string text)
        {
            if (m_MessagesContainer == null) return;
            
            VisualElement row = CreateMessageRow(false);
            row.Add(CreateAvatar("🤖", "avatar-assistant"));
            row.Add(CreateMessageBubble(text, "assistant-message"));
            
            m_MessagesContainer.Add(row);
            ScrollToBottom();
        }
        
        private void AddDebugMessage(string text, string debugClass)
        {
            if (m_MessagesContainer == null || !m_DebugMode) return;
            
            Label message = new Label(text);
            message.AddToClassList("debug-message");
            message.AddToClassList(debugClass);
            message.style.whiteSpace = WhiteSpace.Normal;
            
            m_MessagesContainer.Add(message);
            ScrollToBottom();
        }
        
        private void AddToolMessage(string text)
        {
            if (m_MessagesContainer == null) return;
            
            Label message = new Label(text);
            message.AddToClassList("tool-message");
            message.style.whiteSpace = WhiteSpace.Normal;
            
            m_MessagesContainer.Add(message);
            ScrollToBottom();
        }
        
        private void AddSystemMessage(string text)
        {
            if (m_MessagesContainer == null) return;
            
            Label message = new Label(text);
            message.AddToClassList("system-message");
            message.style.whiteSpace = WhiteSpace.Normal;
            
            m_MessagesContainer.Add(message);
            ScrollToBottom();
        }
        
        private VisualElement CreateMessageRow(bool isUser)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("message-row");
            row.AddToClassList(isUser ? "message-row-user" : "message-row-assistant");
            return row;
        }
        
        private VisualElement CreateAvatar(string label, string className)
        {
            VisualElement avatar = new VisualElement();
            avatar.AddToClassList("avatar");
            avatar.AddToClassList(className);
            
            Label avatarLabel = new Label(label);
            avatarLabel.AddToClassList("avatar-label");
            avatar.Add(avatarLabel);
            
            return avatar;
        }
        
        private VisualElement CreateMessageBubble(string text, string className)
        {
            VisualElement bubble = new VisualElement();
            bubble.AddToClassList("message-bubble");
            bubble.AddToClassList(className);
            
            Label content = new Label(text);
            content.style.whiteSpace = WhiteSpace.Normal;
            content.enableRichText = true;
            bubble.Add(content);
            
            Label timestamp = new Label(DateTime.Now.ToString("HH:mm"));
            timestamp.AddToClassList("timestamp");
            timestamp.AddToClassList(className.Contains("user") ? "timestamp-user" : "timestamp-assistant");
            bubble.Add(timestamp);
            
            return bubble;
        }
        
        private void ShowLoadingIndicator()
        {
            if (m_MessagesContainer == null) return;
            
            m_LoadingIndicator = new VisualElement();
            m_LoadingIndicator.AddToClassList("loading-container");
            m_LoadingIndicator.name = "loading-indicator";
            
            VisualElement avatar = CreateAvatar("🤖", "avatar-assistant");
            m_LoadingIndicator.Add(avatar);
            
            Label loadingLabel = new Label("typing");
            loadingLabel.AddToClassList("loading-label");
            m_LoadingIndicator.Add(loadingLabel);
            
            VisualElement dots = new VisualElement();
            dots.AddToClassList("loading-dots");
            for (int i = 0; i < 3; i++)
            {
                VisualElement dot = new VisualElement();
                dot.AddToClassList("loading-dot");
                dots.Add(dot);
            }
            m_LoadingIndicator.Add(dots);
            
            m_MessagesContainer.Add(m_LoadingIndicator);
            ScrollToBottom();
            
            // Animate dots
            AnimateLoadingDots();
        }
        
        private void HideLoadingIndicator()
        {
            if (m_LoadingIndicator != null)
            {
                m_LoadingIndicator.RemoveFromHierarchy();
                m_LoadingIndicator = null;
            }
        }
        
        private async void AnimateLoadingDots()
        {
            int dotIndex = 0;
            while (m_LoadingIndicator != null && m_IsProcessing)
            {
                if (m_LoadingIndicator == null) break;
                
                var dots = m_LoadingIndicator.Query<VisualElement>(className: "loading-dot").ToList();
                foreach (var dot in dots)
                {
                    dot.RemoveFromClassList("loading-dot-active");
                }
                
                if (dots.Count > dotIndex)
                {
                    dots[dotIndex].AddToClassList("loading-dot-active");
                }
                
                dotIndex = (dotIndex + 1) % 3;
                await Task.Delay(300);
            }
        }
        
        private void ScrollToBottom()
        {
            EditorApplication.delayCall += () =>
            {
                if (m_MessagesScroll != null && m_MessagesContainer != null && m_MessagesContainer.childCount > 0)
                {
                    var lastChild = m_MessagesContainer[m_MessagesContainer.childCount - 1];
                    m_MessagesScroll.ScrollTo(lastChild);
                }
            };
        }
        
        private void ClearMessages()
        {
            m_MessagesContainer?.Clear();
        }
        
        private string SerializeArgs(Dictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            List<string> parts = new List<string>();
            foreach (var kvp in args)
            {
                string value = kvp.Value?.ToString() ?? "null";
                if (value.Length > 50) value = value.Substring(0, 47) + "...";
                parts.Add($"{kvp.Key}: {value}");
            }
            return string.Join(", ", parts);
        }
    }
}
