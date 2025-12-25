# Sentinel: AI-Powered UI Testing Framework for Unity

**Sentinel** is an intelligent, conversational testing agent built for Unity. It allows developers to perform UI testing using natural language inside the Unity Editor, supporting both **UI Toolkit** and **Canvas (uGUI)** systems.

---

## 🎯 Key Features

- **🗣️ Conversational Testing**: Talk to Sentinel in plain language. Ask it to "Click the play button", "Log in with user 'admin'", or "Verify that the main menu is visible".
- **🔄 Universal UI System Support**:
    - **UI Toolkit**: Automatic discovery of VisualElements.
    - **Canvas (uGUI)**: Support for legacy UI and TextMeshPro elements.
- **🤖 Autonomous Agent Loop**: (Roadmap) Sequential execution of complex test plans with automatic verification.
- **📄 Professional Reporting**: Generates Markdown reports for every test run, including:
    - Step-by-step logs with timestamps.
    - Automatic screenshots for key moments and verifications.
    - Success/Failure status and summaries.
- **🎨 Premium Editor Interface**: A modern, dark-themed chat window integrated directly into the Unity Editor.
    - Debug mode to see the agent's "Thinking" process and tool calls.
    - Character counter and intuitive controls (Enter to send, Ctrl+Enter for new line).

---

## 🔧 Core Components

### 1. UI Inspector Service
The brain of the framework. It scans the current scene (Play Mode or Editor Mode) and returns a JSON hierarchy of:
- Buttons, InputFields (legacy & TMP), Toggles, Sliders.
- Static Text elements (for context and verification).
- Hierarchy paths for stable referencing.

### 2. UI Interactor Service
The hands of the framework. It simulates user input:
- **ClickAsync**: Simulates pointer events for UI Toolkit and `onClick`/`IPointerClickHandler` for Canvas.
- **TypeAsync**: Handles text input for `TextField`, `InputField`, and `TMP_InputField`.
- **ScrollAsync**: Controls `ScrollView` and `ScrollRect` components.
- **WaitForElementAsync**: Polling-based wait for dynamic UI transitions.

### 3. Test Report Service
The record-keeper. It manages the report lifecycle:
- Captures screenshots in both **Edit Mode** (Editor views) and **Play Mode** (Game view).
- Generates structured Markdown files in `Assets/TestReports`.
- Refreshes the Asset Database automatically for immediate feedback.

---

## 🚀 Getting Started

### 1. Requirements
- Unity 2022.3+
- OpenAI or QWEN API Key (configured in `AgentConfig`)
- TextMeshPro (included in most projects)

### 2. Setup
1. Create a `SentinelAgentConfig.asset` via the Create menu.
2. Assign the `SentinelSystemPrompt.asset`.
3. Add the required Tool configurations (`Click`, `QueryUI`, `Screenshot`, etc.).
4. Open the chat window via `Window -> LLM -> Agent Chat`.
5. Select your Config and start testing!

---

## 🔧 Available Tools (MCP Compliant)

| Tool | Description |
| :--- | :--- |
| `query_ui` | Scans the UI for visible and interactable elements. |
| `get_editor_state` | Returns Play Mode status, current scene, and compiler state. |
| `click` | Simulates a click on a UI element by path or name. |
| `type_text` | Enters text into an input field. |
| `screenshot` | Captures the current view and saves it to the report. |
| `wait_for_element` | Waits until a specific element appears or becomes visible. |
| `start_test` / `finish_test` | Marks the boundaries of a test case for reporting. |

---

## 🗺️ Roadmap: Multi-Agent System (MAS)

We are currently developing a MAS architecture to enable fully autonomous testing:
- **Planner Agent**: Analyzes the UI and creates a structured test plan.
- **Executor Agent**: Performs the steps one by one.
- **Verifier Agent**: Checks if the UI state matches expectations after each action.

---

*Developed with ❤️ as part of the Unity LLM Agentic Coding project.*
